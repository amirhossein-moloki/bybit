using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Serilog;
using TL;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Exceptions;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using TradingBot.Telegram.Client;

namespace TradingBot.Telegram.Authentication;

public class TelegramAuthService : ITelegramAuthenticationService
{
    private readonly ITelegramClient _client;
    private readonly ITelegramSessionManager _sessionManager;
    private readonly TelegramOptions _options;
    private readonly ILogger _logger;

    private string? _pendingPhoneNumber;
    private string? _pendingPhoneCodeHash;
    private string? _pendingVerificationCode;

    public TelegramAuthService(
        ITelegramClient client,
        ITelegramSessionManager sessionManager,
        IOptions<TelegramOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = Log.ForContext<TelegramAuthService>();
    }

    public async Task AuthenticateAsync()
    {
        if (!_options.Enabled)
        {
            _logger.Warning("Telegram integration is disabled. Skipping passive session authentication.");
            return;
        }

        if (_client is not TelegramClientService clientService)
        {
            throw new TelegramAuthenticationException("ITelegramClient implementation is not of type TelegramClientService.");
        }

        if (clientService.IsInFloodWait(out var remaining))
        {
            _logger.Warning("Telegram client is in FLOOD_WAIT cooldown for {Seconds}s. Passive authentication check deferred.", Math.Ceiling(remaining.TotalSeconds));
            return;
        }

        try
        {
            if (!clientService.IsConnected())
            {
                clientService.SetState(TelegramConnectionState.Connecting);
                await clientService.ConnectAsync();
            }

            var underlyingClient = clientService.UnderlyingClient;
            if (underlyingClient == null)
            {
                clientService.SetState(TelegramConnectionState.Error);
                throw new TelegramAuthenticationException("Underlying WTelegram client is not initialized.");
            }

            // Passive check ONLY on existing user/session.
            // Under NO circumstances call LoginUserIfNeeded() or trigger Auth_SendCode!
            if (underlyingClient.User != null)
            {
                clientService.SetState(TelegramConnectionState.Connected);
                _logger.Information("Telegram passive session verification succeeded for user {UserId} (@{Username}).", underlyingClient.User.id, underlyingClient.User.username);
            }
            else
            {
                clientService.SetState(TelegramConnectionState.RequiresAuthentication);
                _logger.Warning("Telegram passive session verification failed: No active user session found in session file. Manual authentication (OTP/QR) via Dashboard is required.");
            }
        }
        catch (RpcException rpcEx) when (rpcEx.Code == 420 || rpcEx.Message.Contains("FLOOD_WAIT"))
        {
            int waitSeconds = ExtractFloodWaitSeconds(rpcEx);
            clientService.SetFloodWait(waitSeconds);
            clientService.SetState(TelegramConnectionState.Error);
            _logger.Error(rpcEx, "Telegram FLOOD_WAIT_420 encountered during passive session authentication. Cooldown active for {WaitSeconds}s.", waitSeconds);
            throw;
        }
        catch (Exception ex) when (ex is not TelegramAuthenticationException)
        {
            clientService.SetState(TelegramConnectionState.AuthenticationFailed);
            _logger.Error(ex, "Telegram passive session authentication failed.");
            throw new TelegramAuthenticationException("Failed to complete Telegram session authentication.", ex);
        }
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        if (!_options.Enabled) return false;

        if (_client is not TelegramClientService clientService) return false;

        var underlyingClient = clientService.UnderlyingClient;
        if (underlyingClient == null) return false;

        try
        {
            return underlyingClient.User != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<OtpStartResult> StartOtpLoginAsync(string phoneNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new OtpStartResult { Success = false, Error = "Phone number is required." };
        }

        phoneNumber = NormalizePhoneNumber(phoneNumber);
        string maskedPhone = MaskPhoneNumber(phoneNumber);
        _logger.Information("Initiating Telegram OTP login for phone number {MaskedPhone}", maskedPhone);

        if (_client is not TelegramClientService clientService)
        {
            return new OtpStartResult { Success = false, Error = "Invalid Telegram client configuration." };
        }

        try
        {
            clientService.SetState(TelegramConnectionState.Authenticating);

            if (clientService.UnderlyingClient == null)
            {
                await clientService.ConnectAsync();
            }

            var underlyingClient = clientService.UnderlyingClient;
            if (underlyingClient == null)
            {
                return new OtpStartResult { Success = false, Error = "Failed to initialize Telegram client." };
            }

            if (underlyingClient.User != null)
            {
                clientService.SetState(TelegramConnectionState.Connected);
                _logger.Information("Telegram client is already authenticated for user {UserId}", underlyingClient.User.id);
                return new OtpStartResult
                {
                    Success = true,
                    PhoneCodeHash = "authenticated",
                    Message = "Already authenticated"
                };
            }

            // A new request invalidates any code from a prior OTP attempt. The
            // verification endpoint must only advance this freshly-started flow.
            _pendingPhoneNumber = phoneNumber;
            _pendingPhoneCodeHash = null;
            _pendingVerificationCode = null;

            _logger.Information("Calling WTelegram Login({MaskedPhone}) to request OTP code...", maskedPhone);
            var loginState = await underlyingClient.Login(phoneNumber);

            if (loginState is "verification_code")
            {
                _logger.Information("OTP verification code successfully sent via Telegram for {MaskedPhone}", maskedPhone);
                _pendingPhoneCodeHash = "sent";

                return new OtpStartResult
                {
                    Success = true,
                    PhoneCodeHash = "sent",
                    Message = "Verification code sent"
                };
            }

            if (loginState == null && underlyingClient.User != null)
            {
                clientService.SetState(TelegramConnectionState.Connected);
                _logger.Information("Telegram OTP authentication completed immediately for user {UserId}", underlyingClient.User.id);
                return new OtpStartResult
                {
                    Success = true,
                    PhoneCodeHash = "authenticated",
                    Message = "Already authenticated"
                };
            }

            _logger.Warning("WTelegram Login({MaskedPhone}) returned unexpected state: {LoginState}", maskedPhone, loginState);
            return new OtpStartResult { Success = false, Error = $"Unexpected login state: {loginState}" };
        }
        catch (RpcException rpcEx)
        {
            _logger.Warning(rpcEx, "Telegram RPC Error during StartOtpLogin for {MaskedPhone}: Code {Code}, Message {Message}", maskedPhone, rpcEx.Code, rpcEx.Message);

            if (rpcEx.Code == 420 || rpcEx.Message.Contains("FLOOD_WAIT"))
            {
                int waitSeconds = ExtractFloodWaitSeconds(rpcEx);
                clientService.SetFloodWait(waitSeconds);
                clientService.SetState(TelegramConnectionState.Error);
                string waitMsg = $"Too many login attempts (FLOOD_WAIT). Please wait {waitSeconds} seconds before trying again.";
                return new OtpStartResult { Success = false, Error = waitMsg };
            }

            string userMsg = MapRpcErrorToMessage(rpcEx);
            clientService.SetState(TelegramConnectionState.AuthenticationFailed);
            return new OtpStartResult { Success = false, Error = userMsg };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start Telegram OTP login for {MaskedPhone}", maskedPhone);
            clientService.SetState(TelegramConnectionState.AuthenticationFailed);
            return new OtpStartResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<OtpVerifyResult> VerifyOtpAsync(string phoneNumber, string phoneCodeHash, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(code))
        {
            return new OtpVerifyResult { Success = false, Error = "Phone number and code are required." };
        }

        phoneNumber = NormalizePhoneNumber(phoneNumber);
        code = code.Trim();
        string maskedPhone = MaskPhoneNumber(phoneNumber);

        _logger.Information("Verifying Telegram OTP code for {MaskedPhone}", maskedPhone);

        if (!string.Equals(_pendingPhoneNumber, phoneNumber, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(_pendingPhoneCodeHash) ||
            !string.Equals(_pendingPhoneCodeHash, phoneCodeHash, StringComparison.Ordinal))
        {
            return new OtpVerifyResult
            {
                Success = false,
                Error = "Request a new verification code before submitting a code."
            };
        }

        if (_client is not TelegramClientService clientService)
        {
            return new OtpVerifyResult { Success = false, Error = "Invalid Telegram client configuration." };
        }

        try
        {
            clientService.SetState(TelegramConnectionState.Authenticating);

            if (clientService.UnderlyingClient == null)
            {
                await clientService.ConnectAsync();
            }

            var underlyingClient = clientService.UnderlyingClient;
            if (underlyingClient == null)
            {
                return new OtpVerifyResult { Success = false, Error = "Failed to initialize Telegram client." };
            }

            _pendingPhoneNumber = phoneNumber;
            _pendingPhoneCodeHash = string.IsNullOrWhiteSpace(phoneCodeHash) ? _pendingPhoneCodeHash : phoneCodeHash;
            _pendingVerificationCode = code;

            _logger.Information("Calling WTelegram Login(code) for {MaskedPhone}...", maskedPhone);
            var loginState = await underlyingClient.Login(code);

            if (loginState is "password")
            {
                _logger.Information("Two-Factor Authentication (2FA) password required for {MaskedPhone}", maskedPhone);
                return new OtpVerifyResult
                {
                    Success = false,
                    RequiresPassword = true,
                    Error = "Two-factor authentication required"
                };
            }

            if (loginState == null && underlyingClient.User != null)
            {
                clientService.SetState(TelegramConnectionState.Connected);
                _logger.Information("Telegram OTP authentication completed successfully for user {UserId}", underlyingClient.User.id);

                return new OtpVerifyResult
                {
                    Success = true,
                    Status = "Authenticated"
                };
            }

            clientService.SetState(TelegramConnectionState.AuthenticationFailed);
            _logger.Warning("WTelegram Login(code) for {MaskedPhone} returned state: {LoginState}", maskedPhone, loginState);
            return new OtpVerifyResult { Success = false, Error = $"Login returned state: {loginState}" };
        }
        catch (RpcException rpcEx)
        {
            _logger.Warning(rpcEx, "Telegram RPC Error during VerifyOtp for {MaskedPhone}: Code {Code}, Message {Message}", maskedPhone, rpcEx.Code, rpcEx.Message);

            if (rpcEx.Message.Contains("SESSION_PASSWORD_NEEDED"))
            {
                _logger.Information("Telegram RPC SESSION_PASSWORD_NEEDED intercepted for {MaskedPhone}", maskedPhone);
                return new OtpVerifyResult
                {
                    Success = false,
                    RequiresPassword = true,
                    Error = "Two-factor authentication required"
                };
            }

            if (rpcEx.Code == 420 || rpcEx.Message.Contains("FLOOD_WAIT"))
            {
                int waitSeconds = ExtractFloodWaitSeconds(rpcEx);
                clientService.SetFloodWait(waitSeconds);
                clientService.SetState(TelegramConnectionState.Error);
                string waitMsg = $"Too many login attempts (FLOOD_WAIT). Please wait {waitSeconds} seconds before trying again.";
                return new OtpVerifyResult { Success = false, Error = waitMsg };
            }

            string userMsg = MapRpcErrorToMessage(rpcEx);
            clientService.SetState(TelegramConnectionState.AuthenticationFailed);
            return new OtpVerifyResult { Success = false, Error = userMsg };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error verifying Telegram OTP for {MaskedPhone}", maskedPhone);
            clientService.SetState(TelegramConnectionState.AuthenticationFailed);
            return new OtpVerifyResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<PasswordResult> VerifyPasswordAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return new PasswordResult { Success = false, Error = "Password is required." };
        }

        string maskedPhone = MaskPhoneNumber(_pendingPhoneNumber ?? string.Empty);
        _logger.Information("Verifying 2FA password for Telegram user {MaskedPhone}", maskedPhone);

        if (_client is not TelegramClientService clientService)
        {
            return new PasswordResult { Success = false, Error = "Invalid Telegram client configuration." };
        }

        try
        {
            clientService.SetState(TelegramConnectionState.Authenticating);

            if (clientService.UnderlyingClient == null)
            {
                await clientService.ConnectAsync();
            }

            var underlyingClient = clientService.UnderlyingClient;
            if (underlyingClient == null)
            {
                return new PasswordResult { Success = false, Error = "Failed to initialize Telegram client." };
            }

            _logger.Information("Calling WTelegram Login(password) for {MaskedPhone}...", maskedPhone);
            var loginState = await underlyingClient.Login(password);

            if (loginState == null && underlyingClient.User != null)
            {
                clientService.SetState(TelegramConnectionState.Connected);
                _logger.Information("Telegram 2FA authentication completed successfully for user {UserId}", underlyingClient.User.id);

                return new PasswordResult
                {
                    Success = true,
                    Status = "Authenticated"
                };
            }

            clientService.SetState(TelegramConnectionState.AuthenticationFailed);
            _logger.Warning("WTelegram Login(password) for {MaskedPhone} returned state: {LoginState}", maskedPhone, loginState);
            return new PasswordResult { Success = false, Error = $"Password verification returned state: {loginState}" };
        }
        catch (RpcException rpcEx)
        {
            _logger.Warning(rpcEx, "Telegram RPC Error during Password Verification for {MaskedPhone}: Code {Code}, Message {Message}", maskedPhone, rpcEx.Code, rpcEx.Message);

            if (rpcEx.Code == 420 || rpcEx.Message.Contains("FLOOD_WAIT"))
            {
                int waitSeconds = ExtractFloodWaitSeconds(rpcEx);
                clientService.SetFloodWait(waitSeconds);
                clientService.SetState(TelegramConnectionState.Error);
                string waitMsg = $"Too many login attempts (FLOOD_WAIT). Please wait {waitSeconds} seconds before trying again.";
                return new PasswordResult { Success = false, Error = waitMsg };
            }

            string userMsg = MapRpcErrorToMessage(rpcEx);
            clientService.SetState(TelegramConnectionState.AuthenticationFailed);
            return new PasswordResult { Success = false, Error = userMsg };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error verifying 2FA password for {MaskedPhone}", maskedPhone);
            clientService.SetState(TelegramConnectionState.AuthenticationFailed);
            return new PasswordResult { Success = false, Error = ex.Message };
        }
    }

    private static string NormalizePhoneNumber(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var cleaned = input.Trim().Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
        if (!cleaned.StartsWith("+"))
        {
            if (cleaned.StartsWith("00"))
            {
                cleaned = "+" + cleaned.Substring(2);
            }
            else
            {
                cleaned = "+" + cleaned;
            }
        }
        return cleaned;
    }

    private static string MaskPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) return "***";
        if (phoneNumber.Length <= 4) return "****";
        return string.Concat(new string('*', phoneNumber.Length - 4), phoneNumber.Substring(phoneNumber.Length - 4));
    }

    public static int ExtractFloodWaitSeconds(RpcException rpcEx)
    {
        if (rpcEx == null) return 0;

        if (rpcEx.Code == 420 && rpcEx.X > 0)
        {
            return rpcEx.X;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            rpcEx.Message,
            @"FLOOD_WAIT_(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success && int.TryParse(match.Groups[1].Value, out int seconds))
        {
            return seconds;
        }

        return rpcEx.Code == 420 ? 60 : 0;
    }

    private static string MapRpcErrorToMessage(RpcException ex)
    {
        string msg = ex.Message.ToUpperInvariant();

        if (msg.Contains("PHONE_CODE_INVALID"))
        {
            Log.ForContext<TelegramAuthService>().Warning("Telegram RPC Error: PHONE_CODE_INVALID - Provided code is incorrect.");
            return "Invalid verification code.";
        }
        if (msg.Contains("PHONE_CODE_EXPIRED"))
        {
            Log.ForContext<TelegramAuthService>().Warning("Telegram RPC Error: PHONE_CODE_EXPIRED - Verification code expired.");
            return "Verification code expired. Please request a new code.";
        }
        if (msg.Contains("SESSION_PASSWORD_NEEDED"))
        {
            Log.ForContext<TelegramAuthService>().Information("Telegram RPC Info: SESSION_PASSWORD_NEEDED - 2FA password required.");
            return "Two-factor authentication required.";
        }
        if (msg.Contains("FLOOD_WAIT"))
        {
            Log.ForContext<TelegramAuthService>().Warning("Telegram RPC Error: FLOOD_WAIT - Too many login attempts: {Message}", ex.Message);
            return "Too many login attempts. Please wait before trying again.";
        }
        if (msg.Contains("PHONE_NUMBER_BANNED") || msg.Contains("PHONE_BANNED"))
        {
            Log.ForContext<TelegramAuthService>().Error("Telegram RPC Error: PHONE_NUMBER_BANNED - Phone number is banned by Telegram.");
            return "Phone number is banned by Telegram.";
        }
        if (msg.Contains("PHONE_NUMBER_INVALID") || msg.Contains("PHONE_INVALID"))
        {
            Log.ForContext<TelegramAuthService>().Warning("Telegram RPC Error: PHONE_NUMBER_INVALID - Invalid phone number format.");
            return "Invalid phone number format.";
        }
        if (msg.Contains("PASSWORD_HASH_INVALID"))
        {
            Log.ForContext<TelegramAuthService>().Warning("Telegram RPC Error: PASSWORD_HASH_INVALID - Incorrect 2FA password.");
            return "Incorrect password.";
        }

        Log.ForContext<TelegramAuthService>().Warning(ex, "Telegram RPC Exception occurred: Code {Code}, Message {Message}", ex.Code, ex.Message);
        return ex.Message;
    }
}
