using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Services;

public class ConversationContextManager : IConversationContextManager
{
    private readonly IMessageRepository _messageRepository;
    private readonly ISignalContextRepository _signalContextRepository;

    public ConversationContextManager(
        IMessageRepository messageRepository,
        ISignalContextRepository signalContextRepository)
    {
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _signalContextRepository = signalContextRepository ?? throw new ArgumentNullException(nameof(signalContextRepository));
    }

    public async Task<ConversationContext> GetContextAsync(long channelId, CancellationToken cancellationToken = default)
    {
        var recentMessages = await _messageRepository.GetRecentMessagesForChannelAsync(channelId, 10, cancellationToken);
        var activeSignals = await _signalContextRepository.GetActiveContextsForChannelAsync(channelId, cancellationToken);

        var sortedMessages = recentMessages.OrderBy(m => m.ReceivedAt).ToList();

        var context = new ConversationContext
        {
            ChannelId = channelId,
            Messages = sortedMessages,
            ActiveSignals = activeSignals
        };

        var latestActive = activeSignals.OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt).FirstOrDefault();
        if (latestActive != null)
        {
            context.LastSymbol = latestActive.Symbol;
            context.LastSignalId = latestActive.SignalId;
            context.LastAction = latestActive.LastAction ?? string.Empty;
            context.UpdatedAt = latestActive.UpdatedAt ?? latestActive.CreatedAt;
        }

        return context;
    }

    public string GetContextSummary(ConversationContext context)
    {
        if (context == null) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"Channel ID: {context.ChannelId}");

        if (context.ActiveSignals.Any())
        {
            sb.AppendLine("Active Signals in Channel:");
            foreach (var sig in context.ActiveSignals)
            {
                sb.AppendLine($"- Symbol: {sig.Symbol}, State: {sig.CurrentState}, Last Action: {sig.LastAction ?? "None"} (Signal ID: {sig.SignalId})");
            }
        }
        else
        {
            sb.AppendLine("No Active Signals in Channel.");
        }

        if (context.Messages.Any())
        {
            sb.AppendLine("Recent Conversation History:");
            foreach (var msg in context.Messages)
            {
                sb.AppendLine($"[{msg.ReceivedAt:yyyy-MM-dd HH:mm:ss}] {msg.Content}");
            }
        }

        return sb.ToString();
    }
}
