using System.Threading;
using System.Threading.Tasks;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Interfaces;

public interface ITelegramAuthenticationService
{
    Task AuthenticateAsync();
    Task<bool> IsAuthenticatedAsync();
    Task<OtpStartResult> StartOtpLoginAsync(string phoneNumber, CancellationToken ct = default);
    Task<OtpVerifyResult> VerifyOtpAsync(string phoneNumber, string phoneCodeHash, string code, CancellationToken ct = default);
    Task<PasswordResult> VerifyPasswordAsync(string password, CancellationToken ct = default);
}
