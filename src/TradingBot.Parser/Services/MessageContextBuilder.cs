using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Models;
using TradingBot.Telegram.Models;
using AppRepos = TradingBot.Application.Repositories;

namespace TradingBot.Parser.Services;

public class MessageContextBuilder : IMessageContextBuilder
{
    private readonly IMessageRepository? _messageRepository;
    private readonly AppRepos.ISignalRepository? _signalRepository;
    private readonly AppRepos.IPositionRepository? _positionRepository;
    private readonly AppRepos.IOrderRepository? _orderRepository;
    private readonly ITelegramSourceRepository? _sourceRepository;
    private readonly ILogger<MessageContextBuilder> _logger;

    public MessageContextBuilder(
        ILogger<MessageContextBuilder> logger,
        IMessageRepository? messageRepository = null,
        AppRepos.ISignalRepository? signalRepository = null,
        AppRepos.IPositionRepository? positionRepository = null,
        AppRepos.IOrderRepository? orderRepository = null,
        ITelegramSourceRepository? sourceRepository = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _messageRepository = messageRepository;
        _signalRepository = signalRepository;
        _positionRepository = positionRepository;
        _orderRepository = orderRepository;
        _sourceRepository = sourceRepository;
    }

    public async Task<MessageIntelligenceContext> BuildContextAsync(
        TelegramMessageDto message,
        CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext
            {
                MessageId = message.MessageId,
                ChannelId = message.ChannelId,
                SenderId = message.SenderId,
                Text = message.Text,
                Date = message.Date,
                ReplyToMessageId = message.ReplyToMessageId
            }
        };

        // 1. Channel Context
        if (_sourceRepository != null)
        {
            try
            {
                var source = await _sourceRepository.GetByChatIdAsync(message.ChannelId);
                if (source != null)
                {
                    context.ChannelContext = new ChannelContextInfo
                    {
                        ChannelId = source.TelegramChatId,
                        Title = source.Title,
                        ProcessMessages = source.ProcessMessages,
                        ListenForSignals = source.ListenForSignals
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MessageContextBuilder: Failed to resolve ChannelContext for ChannelId {ChannelId}", message.ChannelId);
            }
        }

        // 2. Reply Context
        if (message.ReplyToMessageId.HasValue && message.ReplyToMessageId.Value > 0)
        {
            context.ReplyContext = new ReplyContextInfo
            {
                ReplyToMessageId = message.ReplyToMessageId.Value
            };

            if (_messageRepository != null)
            {
                try
                {
                    var origMsg = await _messageRepository.GetByChannelMessageIdAsync(message.ChannelId, message.ReplyToMessageId.Value, cancellationToken);
                    if (origMsg != null)
                    {
                        context.ReplyContext.OriginalMessageText = origMsg.Content;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MessageContextBuilder: Failed to resolve OriginalMessageText for ReplyToMessageId {ReplyId}", message.ReplyToMessageId.Value);
                }
            }

            if (_signalRepository != null)
            {
                try
                {
                    var pendingSignals = await _signalRepository.GetPendingSignalsAsync(cancellationToken);
                    var origSignal = pendingSignals?.FirstOrDefault(s => s.TelegramChannelId == message.ChannelId && s.TelegramMessageId == message.ReplyToMessageId.Value);
                    if (origSignal != null)
                    {
                        context.ReplyContext.AssociatedSignalId = origSignal.Id;
                        context.ReplyContext.AssociatedSignalStatus = origSignal.Status.ToString();
                        context.ReplyContext.Symbol = origSignal.Symbol;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MessageContextBuilder: Failed to resolve AssociatedSignal for ReplyToMessageId {ReplyId}", message.ReplyToMessageId.Value);
                }
            }
        }

        // 3. Conversation Context (recent messages from channel)
        if (_messageRepository != null)
        {
            try
            {
                var recentMsgs = await _messageRepository.GetRecentMessagesForChannelAsync(message.ChannelId, 5, cancellationToken);
                if (recentMsgs != null)
                {
                    context.ConversationContext = recentMsgs
                        .Where(m => m.MessageId != message.MessageId)
                        .Select(m => new CurrentMessageContext
                        {
                            MessageId = m.MessageId,
                            ChannelId = m.ChannelId,
                            SenderId = m.SenderId,
                            Text = m.Content,
                            Date = m.ReceivedAt,
                            ReplyToMessageId = m.ReplyToMessageId
                        })
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MessageContextBuilder: Failed to resolve ConversationContext for ChannelId {ChannelId}", message.ChannelId);
            }
        }

        // 4. Signal Context (recent signals from channel)
        if (_signalRepository != null)
        {
            try
            {
                var pendingSignals = await _signalRepository.GetPendingSignalsAsync(cancellationToken);
                if (pendingSignals != null)
                {
                    context.RelatedSignals = pendingSignals
                        .Where(s => s.TelegramChannelId == message.ChannelId)
                        .Take(5)
                        .Select(s => new SignalContextInfo
                        {
                            Id = s.Id,
                            MessageId = s.TelegramMessageId ?? 0,
                            Symbol = s.Symbol,
                            Side = s.Side.ToString(),
                            EntryPrice = s.EntryPrice,
                            StopLoss = s.StopLoss,
                            TakeProfit = s.TakeProfit,
                            Status = s.Status.ToString(),
                            CreatedAt = s.CreatedAt,
                            UpdatedAt = s.ValidatedAt
                        }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MessageContextBuilder: Failed to resolve RelatedSignals for ChannelId {ChannelId}", message.ChannelId);
            }
        }

        // 5. Active Positions Context
        if (_positionRepository != null)
        {
            try
            {
                var openPositions = await _positionRepository.GetOpenPositionsAsync(cancellationToken);
                if (openPositions != null)
                {
                    context.ActivePositions = openPositions.Select(p => new PositionContextInfo
                    {
                        PositionId = p.Id,
                        Symbol = p.Symbol,
                        Side = p.Side.ToString(),
                        EntryPrice = p.EntryPrice,
                        CurrentStopLoss = p.StopLoss ?? 0m,
                        Quantity = p.Quantity,
                        Status = p.Status.ToString()
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MessageContextBuilder: Failed to resolve ActivePositions");
            }
        }

        // 6. Pending Orders Context
        if (_orderRepository != null)
        {
            try
            {
                var activeOrders = await _orderRepository.GetOpenOrdersAsync(cancellationToken);
                if (activeOrders != null)
                {
                    context.PendingOrders = activeOrders.Select(o => new OrderContextInfo
                    {
                        OrderId = o.Id,
                        Symbol = o.Symbol?.Value ?? string.Empty,
                        Side = o.Side.ToString(),
                        OrderType = o.Type.ToString(),
                        Price = o.Price?.Amount ?? 0m,
                        Quantity = o.Quantity?.Value ?? 0m,
                        Status = o.Status.ToString()
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MessageContextBuilder: Failed to resolve PendingOrders");
            }
        }

        return context;
    }
}
