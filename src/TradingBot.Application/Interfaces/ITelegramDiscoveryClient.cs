using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public interface ITelegramDiscoveryClient
{
    Task<List<DiscoveredTelegramChatDto>> GetDialogsAsync(CancellationToken ct = default);
    bool IsConnected();
    string GetCurrentState();
}

public sealed record DiscoveredTelegramChatDto(
    long Id,
    string Title,
    string? Username,
    bool IsChannel,
    bool IsGroup
);
