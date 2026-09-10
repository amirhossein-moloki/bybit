using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.Models;
using TradingBot.Application.Repositories;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Services;

public class TelegramSourceService : ITelegramSourceService
{
    private readonly ITelegramSourceRepository _repository;
    private readonly ITelegramDiscoveryClient? _discoveryClient;
    private readonly IMessageRepository? _messageRepository;
    private readonly TradingBot.Application.Repositories.ISignalRepository? _signalRepository;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger<TelegramSourceService> _logger;

    public TelegramSourceService(
        ITelegramSourceRepository repository,
        ILogger<TelegramSourceService> logger,
        ITelegramDiscoveryClient? discoveryClient = null,
        IMessageRepository? messageRepository = null,
        TradingBot.Application.Repositories.ISignalRepository? signalRepository = null,
        IServiceProvider? serviceProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _discoveryClient = discoveryClient;
        _messageRepository = messageRepository;
        _signalRepository = signalRepository;
        _serviceProvider = serviceProvider;
    }

    public async Task<List<TelegramSourceDto>> GetSourcesAsync(TelegramSourceFilterDto filter, CancellationToken ct = default)
    {
        var sources = await _repository.GetAllAsync(ct);

        // Apply Search
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            sources = sources.Where(s =>
                s.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (s.Username != null && s.Username.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                s.TelegramChatId.ToString().Contains(search)
            ).ToList();
        }

        // Apply Type Filter
        if (!string.IsNullOrWhiteSpace(filter.Type) && !filter.Type.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<TelegramSourceType>(filter.Type, true, out var sourceType))
            {
                sources = sources.Where(s => s.Type == sourceType).ToList();
            }
        }

        // Apply IsEnabled
        if (filter.IsEnabled.HasValue)
        {
            sources = sources.Where(s => s.IsEnabled == filter.IsEnabled.Value).ToList();
        }

        // Apply ListenForSignals
        if (filter.ListenForSignals.HasValue)
        {
            sources = sources.Where(s => s.ListenForSignals == filter.ListenForSignals.Value).ToList();
        }

        // Apply Status Filter
        if (!string.IsNullOrWhiteSpace(filter.Status) && !filter.Status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var targetStatus = filter.Status.Trim();
            sources = sources.Where(s =>
            {
                var dto = MapToDto(s);
                return dto.Status.Equals(targetStatus, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        // Pagination
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 ? 20 : filter.PageSize;

        var pagedSources = sources
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToDto)
            .ToList();

        return pagedSources;
    }

    public async Task<TelegramSourceDto?> GetSourceByIdAsync(Guid id, CancellationToken ct = default)
    {
        var source = await _repository.GetByIdAsync(id, ct);
        return source == null ? null : MapToDto(source);
    }

    public async Task<TelegramSourceDto> CreateSourceAsync(CreateTelegramSourceDto dto, CancellationToken ct = default)
    {
        var existing = await _repository.GetByChatIdAsync(dto.TelegramChatId, ct);
        if (existing != null)
        {
            throw new InvalidOperationException($"A Telegram source with ChatId '{dto.TelegramChatId}' already exists.");
        }

        if (!Enum.TryParse<TelegramSourceType>(dto.Type, true, out var sourceType))
        {
            sourceType = TelegramSourceType.Channel;
        }

        var source = new TelegramSource(
            dto.TelegramChatId,
            dto.Title,
            dto.Username,
            sourceType,
            dto.IsEnabled,
            dto.ListenForSignals,
            dto.ProcessMessages
        );

        await _repository.AddAsync(source, ct);
        _logger.LogInformation("Telegram source created: {Title} ({ChatId})", source.Title, source.TelegramChatId);

        return MapToDto(source);
    }

    public async Task<TelegramSourceDto> UpdateSourceAsync(Guid id, UpdateTelegramSourceDto dto, CancellationToken ct = default)
    {
        var source = await _repository.GetByIdAsync(id, ct);
        if (source == null)
        {
            throw new KeyNotFoundException($"TelegramSource with ID '{id}' was not found.");
        }

        if (dto.IsEnabled.HasValue)
        {
            source.SetEnabled(dto.IsEnabled.Value);
        }

        if (dto.ListenForSignals.HasValue)
        {
            source.SetListenForSignals(dto.ListenForSignals.Value);
        }

        if (dto.ProcessMessages.HasValue)
        {
            source.SetProcessMessages(dto.ProcessMessages.Value);
        }

        if (dto.PauseMinutes.HasValue)
        {
            if (dto.PauseMinutes.Value > 0)
            {
                source.Pause(TimeSpan.FromMinutes(dto.PauseMinutes.Value));
            }
            else
            {
                source.Resume();
            }
        }

        await _repository.UpdateAsync(source, ct);
        _logger.LogInformation("Telegram source updated: {Title} ({ChatId}). Enabled: {IsEnabled}, Signals: {ListenForSignals}, Process: {ProcessMessages}",
            source.Title, source.TelegramChatId, source.IsEnabled, source.ListenForSignals, source.ProcessMessages);

        return MapToDto(source);
    }

    public async Task<bool> DeleteSourceAsync(Guid id, CancellationToken ct = default)
    {
        var source = await _repository.GetByIdAsync(id, ct);
        if (source == null) return false;

        await _repository.DeleteAsync(source, ct);
        _logger.LogInformation("Telegram source deleted: {Title} ({ChatId})", source.Title, source.TelegramChatId);
        return true;
    }

    public async Task<SyncSourcesResultDto> SyncSourcesAsync(CancellationToken ct = default)
    {
        if (_discoveryClient == null)
        {
            throw new InvalidOperationException("Telegram discovery client is not available for discovery sync.");
        }

        _logger.LogInformation("Starting Telegram sources discovery sync...");

        List<DiscoveredTelegramChatDto> dialogs;
        try
        {
            dialogs = await _discoveryClient.GetDialogsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve Telegram dialogs during sync.");
            throw new InvalidOperationException("Failed to retrieve Telegram dialogs: " + ex.Message, ex);
        }

        int discovered = dialogs.Count;
        int newCount = 0;
        int updatedCount = 0;
        int errorCount = 0;
        var titles = new List<string>();

        foreach (var dialog in dialogs)
        {
            try
            {
                var existing = await _repository.GetByChatIdAsync(dialog.Id, ct);

                TelegramSourceType type;
                if (dialog.IsChannel) type = TelegramSourceType.Channel;
                else if (dialog.IsGroup) type = TelegramSourceType.Group;
                else type = TelegramSourceType.Channel;

                titles.Add(dialog.Title);

                if (existing == null)
                {
                    var newSource = new TelegramSource(
                        dialog.Id,
                        dialog.Title,
                        dialog.Username,
                        type,
                        isEnabled: true,
                        listenForSignals: true,
                        processMessages: true
                    );
                    await _repository.AddAsync(newSource, ct);
                    newCount++;
                }
                else
                {
                    // Update metadata without overwriting capability settings (IsEnabled, ListenForSignals, ProcessMessages, PausedUntil)
                    existing.UpdateMetadata(dialog.Title, dialog.Username, type);
                    await _repository.UpdateAsync(existing, ct);
                    updatedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Telegram chat ID {ChatId} ({Title})", dialog.Id, dialog.Title);
                errorCount++;
            }
        }

        _logger.LogInformation("Telegram sync completed. Discovered: {Discovered}, New: {NewCount}, Updated: {UpdatedCount}, Errors: {ErrorCount}",
            discovered, newCount, updatedCount, errorCount);

        return new SyncSourcesResultDto(discovered, newCount, updatedCount, errorCount, titles);
    }

    public async Task<List<TelegramMessagePreviewDto>> GetSourceMessagesAsync(Guid id, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var source = await _repository.GetByIdAsync(id, ct);
        if (source == null) return new List<TelegramMessagePreviewDto>();

        if (_messageRepository == null) return new List<TelegramMessagePreviewDto>();

        var rawMessages = await _messageRepository.GetRecentMessagesForChannelAsync(source.TelegramChatId, pageSize * page, ct);

        return rawMessages
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new TelegramMessagePreviewDto(
                m.Id,
                m.MessageId,
                m.SenderId,
                m.Content.Length > 100 ? m.Content.Substring(0, 97) + "..." : m.Content,
                m.ReceivedAt,
                m.Processed
            ))
            .ToList();
    }

    public async Task<List<TelegramSignalPreviewDto>> GetSourceSignalsAsync(Guid id, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var source = await _repository.GetByIdAsync(id, ct);
        if (source == null || _signalRepository == null) return new List<TelegramSignalPreviewDto>();

        var pagedSignals = await _signalRepository.GetPagedSignalsAsync(page, pageSize, ct);
        var channelSignals = pagedSignals.Items
            .Where(s => s.TelegramChannelId == source.TelegramChatId)
            .Select(s => new TelegramSignalPreviewDto(
                s.Id,
                s.TelegramMessageId ?? 0,
                s.Symbol,
                s.Side.ToString(),
                1.0,
                s.Status.ToString(),
                s.CreatedAt
            ))
            .ToList();

        return channelSignals;
    }

    public async Task<List<TelegramMessagePipelineItemDto>> GetLiveMessagePipelineAsync(Guid? sourceId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        long? targetChatId = null;
        if (sourceId.HasValue && sourceId.Value != Guid.Empty)
        {
            var source = await _repository.GetByIdAsync(sourceId.Value, ct);
            if (source != null)
            {
                targetChatId = source.TelegramChatId;
            }
        }

        var sources = await _repository.GetAllAsync(ct);
        var channelMap = sources.ToDictionary(s => s.TelegramChatId, s => s.Title);

        if (_messageRepository == null)
        {
            return new List<TelegramMessagePipelineItemDto>();
        }

        using var scope = _serviceProvider?.CreateScope();
        var messageRepo = scope?.ServiceProvider.GetService<IMessageRepository>() ?? _messageRepository;
        var signalRepo = scope?.ServiceProvider.GetService<TradingBot.Application.Repositories.ISignalRepository>() ?? _signalRepository;
        var decisionRepo = scope?.ServiceProvider.GetService<ITradeDecisionRepository>();
        var riskRepo = scope?.ServiceProvider.GetService<IRiskEvaluationRepository>();
        var orderRepo = scope?.ServiceProvider.GetService<TradingBot.Application.Repositories.IOrderRepository>();

        if (messageRepo == null)
        {
            return new List<TelegramMessagePipelineItemDto>();
        }

        List<TradingBot.Domain.SignalIntelligence.Entities.TelegramMessage> messages;
        if (targetChatId.HasValue)
        {
            messages = await messageRepo.GetRecentMessagesForChannelAsync(targetChatId.Value, pageSize * page, ct);
            messages = messages.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }
        else
        {
            var allMessagesList = new List<TradingBot.Domain.SignalIntelligence.Entities.TelegramMessage>();
            foreach (var ch in sources)
            {
                var channelMsgs = await messageRepo.GetRecentMessagesForChannelAsync(ch.TelegramChatId, pageSize, ct);
                allMessagesList.AddRange(channelMsgs);
            }
            messages = allMessagesList
                .OrderByDescending(m => m.ReceivedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        if (!messages.Any())
        {
            return new List<TelegramMessagePipelineItemDto>();
        }

        List<Signal> signals = new List<Signal>();
        if (signalRepo != null)
        {
            var pagedSignals = await signalRepo.GetPagedSignalsAsync(1, 100, ct);
            signals = pagedSignals.Items.ToList();
        }

        List<TradingBot.Domain.RiskManagement.Entities.TradeDecision> decisions = new List<TradingBot.Domain.RiskManagement.Entities.TradeDecision>();
        if (decisionRepo != null)
        {
            var allDecisions = await decisionRepo.GetAllAsync(ct);
            decisions = allDecisions.ToList();
        }

        List<TradingBot.Domain.RiskManagement.Entities.RiskEvaluation> riskEvals = new List<TradingBot.Domain.RiskManagement.Entities.RiskEvaluation>();
        if (riskRepo != null)
        {
            var allRisk = await riskRepo.GetAllAsync(ct);
            riskEvals = allRisk.ToList();
        }

        List<TradingBot.Domain.Entities.Order> orders = new List<TradingBot.Domain.Entities.Order>();
        if (orderRepo != null)
        {
            var allOrders = await orderRepo.GetAllAsync(ct);
            orders = allOrders.ToList();
        }

        var pipelineItems = new List<TelegramMessagePipelineItemDto>();

        foreach (var m in messages)
        {
            channelMap.TryGetValue(m.ChannelId, out var channelTitle);
            channelTitle ??= $"Chat #{m.ChannelId}";

            TelegramSignalDetailDto? signalDto = null;
            TelegramRiskDecisionDto? decisionDto = null;
            TelegramOrderExecutionDto? executionDto = null;

            var sig = signals.FirstOrDefault(s => s.TelegramChannelId == m.ChannelId && s.TelegramMessageId == m.MessageId);
            if (sig != null)
            {
                signalDto = new TelegramSignalDetailDto(
                    sig.Id,
                    sig.Symbol,
                    sig.Side.ToString(),
                    sig.Status.ToString(),
                    sig.EntryPrice,
                    sig.StopLoss,
                    sig.TakeProfit,
                    sig.Leverage,
                    sig.CreatedAt
                );

                var dec = decisions.FirstOrDefault(d => d.SignalId == sig.Id);
                var risk = riskEvals.FirstOrDefault(r => r.SignalId == sig.Id);

                if (dec != null || risk != null)
                {
                    var decisionStr = dec?.Decision.ToString() ?? risk?.Decision.ToString() ?? "Unknown";
                    var reasonStr = dec?.DecisionReason ?? risk?.Reason ?? string.Empty;
                    var posSize = risk?.PositionSize;

                    decisionDto = new TelegramRiskDecisionDto(
                        dec?.Id ?? risk?.Id ?? Guid.NewGuid(),
                        decisionStr,
                        1.0m,
                        reasonStr,
                        posSize,
                        sig.Leverage,
                        dec?.CreatedAt ?? risk?.CreatedAt ?? DateTime.UtcNow
                    );
                }

                var ord = orders.FirstOrDefault(o => o.SignalId == sig.Id);
                if (ord != null)
                {
                    executionDto = new TelegramOrderExecutionDto(
                        ord.Id,
                        ord.Status.ToString(),
                        ord.Quantity.Value,
                        ord.Price?.Amount,
                        ord.ExchangeOrderId,
                        ord.CreatedAt
                    );
                }
            }

            pipelineItems.Add(new TelegramMessagePipelineItemDto(
                m.Id,
                m.ChannelId,
                channelTitle,
                m.MessageId,
                m.SenderId,
                m.Content,
                m.ReceivedAt,
                m.Processed,
                signalDto,
                decisionDto,
                executionDto
            ));
        }

        return pipelineItems;
    }

    public async Task<TelegramSourceHealthDto> GetSourceHealthAsync(Guid id, CancellationToken ct = default)
    {
        var source = await _repository.GetByIdAsync(id, ct);
        if (source == null)
        {
            throw new KeyNotFoundException($"TelegramSource with ID '{id}' was not found.");
        }

        var connStatus = _discoveryClient != null && _discoveryClient.IsConnected() ? "Connected" : "Disconnected";
        var listenerState = _discoveryClient != null ? _discoveryClient.GetCurrentState() : "NotConnected";

        return new TelegramSourceHealthDto(
            connStatus,
            listenerState,
            source.UpdatedAt,
            null,
            0,
            0
        );
    }

    public async Task<TestSourceResultDto> TestSourceAsync(Guid id, CancellationToken ct = default)
    {
        var source = await _repository.GetByIdAsync(id, ct);
        if (source == null)
        {
            return new TestSourceResultDto(
                Success: false,
                TelegramConnected: false,
                SourceAccessible: false,
                MessagesReadable: false,
                ListenerConfigured: false,
                SignalProcessingAvailable: false,
                Message: $"Telegram source with ID '{id}' not found.",
                Details: new List<string> { "Source record missing in database." }
            );
        }

        var details = new List<string>();
        bool isConnected = _discoveryClient != null && _discoveryClient.IsConnected();
        details.Add(isConnected ? "Telegram client is connected." : "Telegram client is NOT connected.");

        bool sourceAccessible = isConnected;
        details.Add(sourceAccessible ? $"Source '{source.Title}' ({source.TelegramChatId}) is accessible." : "Cannot verify source accessibility without Telegram connection.");

        bool messagesReadable = sourceAccessible && source.ProcessMessages;
        details.Add(messagesReadable ? "Message processing is ENABLED." : "Message processing is DISABLED or inaccessible.");

        bool listenerConfigured = source.IsEnabled && !source.IsPaused;
        details.Add(listenerConfigured ? "Listener capability is ENABLED and active." : "Listener is DISABLED or PAUSED.");

        bool signalProcessingAvailable = listenerConfigured && source.ListenForSignals;
        details.Add(signalProcessingAvailable ? "Signal listening capability is ENABLED." : "Signal listening capability is DISABLED.");

        bool overallSuccess = isConnected && sourceAccessible && source.IsEnabled;

        return new TestSourceResultDto(
            Success: overallSuccess,
            TelegramConnected: isConnected,
            SourceAccessible: sourceAccessible,
            MessagesReadable: messagesReadable,
            ListenerConfigured: listenerConfigured,
            SignalProcessingAvailable: signalProcessingAvailable,
            Message: overallSuccess ? $"Test completed successfully for source '{source.Title}'." : $"Test failed or source '{source.Title}' is inactive.",
            Details: details
        );
    }

    public async Task<int> BulkUpdateSourcesAsync(BulkUpdateSourcesDto dto, CancellationToken ct = default)
    {
        if (dto.SourceIds == null || !dto.SourceIds.Any()) return 0;

        int updatedCount = 0;
        foreach (var id in dto.SourceIds)
        {
            var source = await _repository.GetByIdAsync(id, ct);
            if (source == null) continue;

            switch (dto.Action?.ToLowerInvariant())
            {
                case "enable":
                    source.SetEnabled(true);
                    break;
                case "disable":
                    source.SetEnabled(false);
                    break;
                case "enablesignals":
                    source.SetListenForSignals(true);
                    break;
                case "disablesignals":
                    source.SetListenForSignals(false);
                    break;
                case "pause":
                    var pauseMins = dto.PauseMinutes.HasValue && dto.PauseMinutes.Value > 0 ? dto.PauseMinutes.Value : 60;
                    source.Pause(TimeSpan.FromMinutes(pauseMins));
                    break;
                default:
                    continue;
            }

            await _repository.UpdateAsync(source, ct);
            updatedCount++;
        }

        _logger.LogInformation("Bulk update applied action '{Action}' to {Count} sources.", dto.Action, updatedCount);
        return updatedCount;
    }

    public async Task<List<TelegramSource>> GetActiveSourcesAsync(CancellationToken ct = default)
    {
        return await _repository.GetActiveSourcesAsync(ct);
    }

    private static TelegramSourceDto MapToDto(TelegramSource s)
    {
        string status;
        if (!s.IsEnabled)
        {
            status = "Disabled";
        }
        else if (s.IsPaused)
        {
            status = "Paused";
        }
        else
        {
            status = "Listening";
        }

        return new TelegramSourceDto(
            s.Id,
            s.TelegramChatId,
            s.Title,
            s.Username,
            s.Type.ToString(),
            s.IsEnabled,
            s.ListenForSignals,
            s.ProcessMessages,
            s.PausedUntil,
            status,
            s.UpdatedAt,
            0,
            0,
            s.CreatedAt,
            s.UpdatedAt
        );
    }
}
