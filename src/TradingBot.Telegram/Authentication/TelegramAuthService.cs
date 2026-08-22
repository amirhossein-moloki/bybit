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
            _logger.Warning("Telegram integration is disabled. Skipping authentication.");
            return;
        }

        if (_client is not TelegramClientService clientService)
        {
            throw new TelegramAuthenticationException("ITelegramClient implementation is not of type TelegramClientService.");
        }

        try
        {
            // Ensure connection is established
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

            clientService.SetState(TelegramConnectionState.Authenticating);
            _logger.Information("Beginning Telegram login flow...");

            // Call LoginUserIfNeeded to perform authentication flow
            var user = await underlyingClient.LoginUserIfNeeded();

            if (user != null)
            {
                clientService.SetState(TelegramConnectionState.Connected);
                _logger.Information("Authentication Completed");
            }
            else
            {
                clientService.SetState(TelegramConnectionState.AuthenticationFailed);
                throw new TelegramAuthenticationException("Telegram login failed: returned user was null.");
            }
        }
        catch (Exception ex) when (ex is not TelegramAuthenticationException)
        {
            clientService.SetState(TelegramConnectionState.AuthenticationFailed);
            _logger.Error(ex, "Telegram authentication failed.");
            throw new TelegramAuthenticationException("Failed to complete Telegram authentication.", ex);
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

        phoneNumber = phoneNumber.Trim();
        string maskedPhone = MaskPhoneNumber(phoneNumber);
        _logger.Information("Initiating Telegram OTP login for phone number {MaskedPhone}", maskedPhone);

        if (_client is not TelegramClientService clientService)
        {
            return new OtpStartResult { Success = false, Error = "Invalid Telegram client configuration." };
        }

        try
        {
            clientService.SetState(TelegramConnectionState.Authenticating);

            if (!clientService.IsConnected() || clientService.UnderlyingClient == null)
            {
                await clientService.ConnectAsync();
            }

            var underlyingClient = clientService.UnderlyingClient;
            if (underlyingClient == null)
            {
                return new OtpStartResult { Success = false, Error = "Failed to initialize Telegram client." };
            }

            _pendingPhoneNumber = phoneNumber;

            var loginState = await underlyingClient.Login(phoneNumber);

            if (loginState is "verification_code")
            {
                _logger.Information("OTP verification code successfully sent for {MaskedPhone}", maskedPhone);

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
                return new OtpStartResult
                {
                    Success = true,
                    PhoneCodeHash = "authenticated",
                    Message = "Already authenticated"
                };
            }

            _logger.Warning("Login returned state {LoginState} for {MaskedPhone}", loginState, maskedPhone);
            return new OtpStartResult { Success = false, Error = $"Unexpected login state: {loginState}" };
        }
        catch (RpcException rpcEx)
        {
            _logger.Warning(rpcEx, "Telegram RPC Error during StartOtpLogin for {MaskedPhone}: {Message}", maskedPhone, rpcEx.Message);
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

        phoneNumber = phoneNumber.Trim();
        code = code.Trim();
        string maskedPhone = MaskPhoneNumber(phoneNumber);

        _logger.Information("Verifying Telegram OTP code for {MaskedPhone}", maskedPhone);

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
            return new OtpVerifyResult { Success = false, Error = $"Login returned state: {loginState}" };
        }
        catch (RpcException rpcEx)
        {
            _logger.Warning(rpcEx, "Telegram RPC Error during VerifyOtp for {MaskedPhone}: {Message}", maskedPhone, rpcEx.Message);

            if (rpcEx.Message.Contains("SESSION_PASSWORD_NEEDED"))
            {
                return new OtpVerifyResult
                {
                    Success = false,
                    RequiresPassword = true,
                    Error = "Two-factor authentication required"
                };
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
            return new PasswordResult { Success = false, Error = $"Password verification returned state: {loginState}" };
        }
        catch (RpcException rpcEx)
        {
            _logger.Warning(rpcEx, "Telegram RPC Error during Password Verification for {MaskedPhone}: {Message}", maskedPhone, rpcEx.Message);
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

    private static string MaskPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) return "***";
        if (phoneNumber.Length <= 4) return "****";
        return string.Concat(new string('*', phoneNumber.Length - 4), phoneNumber.Substring(phoneNumber.Length - 4));
    }

    private static string MapRpcErrorToMessage(RpcException ex)
    {
        string msg = ex.Message.ToUpperInvariant();

        if (msg.Contains("PHONE_CODE_INVALID")) return "Invalid verification code.";
        if (msg.Contains("PHONE_CODE_EXPIRED")) return "Verification code expired.";
        if (msg.Contains("SESSION_PASSWORD_NEEDED")) return "Two-factor authentication required.";
        if (msg.Contains("FLOOD_WAIT")) return "Too many login attempts. Please wait before trying again.";
        if (msg.Contains("PHONE_NUMBER_BANNED") || msg.Contains("PHONE_BANNED")) return "Phone number is banned by Telegram.";
        if (msg.Contains("PASSWORD_HASH_INVALID")) return "Incorrect password.";

        return ex.Message;
    }
}
