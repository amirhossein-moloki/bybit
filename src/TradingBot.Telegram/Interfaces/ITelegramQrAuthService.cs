using System.Threading;
using System.Threading.Tasks;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Interfaces;

public interface ITelegramQrAuthService
{
    Task<TelegramStatusDto> GetStatusAsync(CancellationToken ct = default);
    Task<TelegramQrStartResultDto> StartQrAuthAsync(CancellationToken ct = default);
    Task<TelegramQrStatusDto> GetQrStatusAsync(string? sessionId = null, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
}
