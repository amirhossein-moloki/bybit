using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;

namespace TradingBot.Worker;

public static class TelegramCliAuth
{
    public static async Task RunAsync(IServiceProvider serviceProvider, TextReader? inputReader = null, TextWriter? outputWriter = null)
    {
        var input = inputReader ?? Console.In;
        var output = outputWriter ?? Console.Out;

        using var scope = serviceProvider.CreateScope();
        var qrAuthService = scope.ServiceProvider.GetService<ITelegramQrAuthService>();
        var authService = scope.ServiceProvider.GetService<ITelegramAuthenticationService>();
        var client = scope.ServiceProvider.GetService<ITelegramClient>();

        if (qrAuthService == null || authService == null || client == null)
        {
            await output.WriteLineAsync("Error: Telegram authentication services are not registered.");
            return;
        }

        await output.WriteLineAsync("=============== TELEGRAM CLI AUTHENTICATION ===============");

        try
        {
            var status = await qrAuthService.GetStatusAsync();
            if (status.Connected && status.Account != null)
            {
                await output.WriteLineAsync($"Telegram is ALREADY CONNECTED as: {status.Account.FirstName} {status.Account.LastName} (@{status.Account.Username}) [Phone: {status.Account.Phone}]");
                await output.WriteAsync("Do you want to re-authenticate or logout? (y/N): ");
                var choice = (await input.ReadLineAsync())?.Trim();
                if (!string.Equals(choice, "y", StringComparison.OrdinalIgnoreCase))
                {
                    await output.WriteLineAsync("Exiting Telegram CLI authentication tool.");
                    return;
                }

                await output.WriteLineAsync("Logging out existing Telegram session...");
                await qrAuthService.LogoutAsync();
                await output.WriteLineAsync("Logged out successfully.");
            }

            await output.WriteLineAsync();
            await output.WriteLineAsync("Select Telegram Login Method:");
            await output.WriteLineAsync("  1) OTP Verification Code (Phone Number)");
            await output.WriteLineAsync("  2) QR Code Scan");
            await output.WriteAsync("Enter selection [1 or 2] (Default: 1): ");

            var selection = (await input.ReadLineAsync())?.Trim();
            if (selection == "2")
            {
                await RunQrFlowAsync(qrAuthService, output);
            }
            else
            {
                await RunOtpFlowAsync(authService, client, input, output);
            }
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("\nAuthentication canceled.");
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"\nAuthentication Error: {ex.Message}");
        }

        await output.WriteLineAsync("==========================================================");
    }

    private static async Task RunOtpFlowAsync(ITelegramAuthenticationService authService, ITelegramClient client, TextReader input, TextWriter output)
    {
        await output.WriteLineAsync("\n--- OTP Login Flow ---");
        await output.WriteAsync("Enter phone number with country code (e.g. +15550100000): ");
        await output.FlushAsync();
        var phoneNumber = (await input.ReadLineAsync())?.Trim();

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            await output.WriteLineAsync("Error: Phone number cannot be empty.");
            return;
        }

        if (client is TradingBot.Telegram.Client.TelegramClientService clientService)
        {
            clientService.PhoneNumberProvider = () => phoneNumber;
            clientService.VerificationCodeProvider = null;
            clientService.PasswordProvider = null;
        }

        await output.WriteLineAsync("Sending verification code...");
        await output.FlushAsync();

        var startResult = await authService.StartOtpLoginAsync(phoneNumber);

        if (!startResult.Success)
        {
            await output.WriteLineAsync($"Failed to start login: {startResult.Error}");
            return;
        }

        if (startResult.PhoneCodeHash == "authenticated")
        {
            await output.WriteLineAsync("\nVerification successful.");
            await output.WriteLineAsync("Telegram authentication completed successfully.");
            return;
        }

        await output.WriteLineAsync("\nA verification code has been sent via Telegram.");
        await output.WriteAsync("Enter verification code: ");
        await output.FlushAsync();

        var promptCode = (await input.ReadLineAsync())?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(promptCode))
        {
            await output.WriteLineAsync("Error: Verification code cannot be empty.");
            return;
        }

        await output.WriteLineAsync("Verifying code...");
        await output.FlushAsync();

        var verifyResult = await authService.VerifyOtpAsync(phoneNumber, startResult.PhoneCodeHash ?? "sent", promptCode);

        if (verifyResult.Success)
        {
            await output.WriteLineAsync("\nVerification successful.");
            await output.WriteLineAsync("Telegram authentication completed successfully.");
            return;
        }

        if (verifyResult.RequiresPassword)
        {
            await output.WriteLineAsync("\nTwo-step verification is enabled.");
            await output.WriteAsync("Enter your Telegram 2FA password: ");
            await output.FlushAsync();

            var promptPwd = await ReadPasswordAsync(input, output);
            if (string.IsNullOrWhiteSpace(promptPwd))
            {
                await output.WriteLineAsync("Error: Password cannot be empty.");
                return;
            }

            await output.WriteLineAsync("Verifying 2FA password...");
            await output.FlushAsync();

            var passResult = await authService.VerifyPasswordAsync(promptPwd);

            if (passResult.Success)
            {
                await output.WriteLineAsync("\nVerification successful.");
                await output.WriteLineAsync("Telegram authentication completed successfully.");
            }
            else
            {
                await output.WriteLineAsync($"2FA Authentication Failed: {passResult.Error}");
            }
            return;
        }

        await output.WriteLineAsync($"Verification Failed: {verifyResult.Error}");
    }

    private static async Task<string> ReadPasswordAsync(TextReader input, TextWriter output)
    {
        if (ReferenceEquals(input, Console.In) && !Console.IsInputRedirected)
        {
            var pwd = new System.Text.StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    await output.WriteLineAsync();
                    break;
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (pwd.Length > 0)
                    {
                        pwd.Remove(pwd.Length - 1, 1);
                    }
                }
                else if (key.KeyChar != '\u0000')
                {
                    pwd.Append(key.KeyChar);
                }
            }
            return pwd.ToString().Trim();
        }

        var line = await input.ReadLineAsync();
        return line?.Trim() ?? string.Empty;
    }

    private static async Task RunQrFlowAsync(ITelegramQrAuthService qrAuthService, TextWriter output)
    {
        await output.WriteLineAsync("\n--- QR Code Login Flow ---");
        await output.WriteLineAsync("Generating Telegram Login QR Code...");

        var startResult = await qrAuthService.StartQrAuthAsync();
        if (string.IsNullOrEmpty(startResult.SessionId))
        {
            await output.WriteLineAsync("Failed to start QR auth session.");
            return;
        }

        await output.WriteLineAsync($"QR Session ID: {startResult.SessionId}");
        if (!string.IsNullOrEmpty(startResult.QrData))
        {
            await output.WriteLineAsync($"Telegram Login Link / QR Data: {startResult.QrData}");
        }

        await output.WriteLineAsync("Open Telegram on your mobile phone -> Settings -> Devices -> Link Desktop Device and scan or open the link above.");
        await output.WriteLineAsync("Waiting for QR Code scan completion (timeout in 2 minutes)...");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var status = await qrAuthService.GetQrStatusAsync(startResult.SessionId, cts.Token);
                if (string.Equals(status.Status, "connected", StringComparison.OrdinalIgnoreCase) || status.Account != null)
                {
                    await output.WriteLineAsync($"\nSUCCESS: Telegram connected as {status.Account?.FirstName} {status.Account?.LastName} (@{status.Account?.Username})!");
                    return;
                }

                if (string.Equals(status.Status, "expired", StringComparison.OrdinalIgnoreCase) || string.Equals(status.Status, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    await output.WriteLineAsync($"\nQR Login Session state: {status.Status}. Error: {status.Error}");
                    return;
                }

                await output.WriteAsync(".");
                await Task.Delay(2000, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await output.WriteLineAsync($"\nError checking QR status: {ex.Message}");
                break;
            }
        }

        await output.WriteLineAsync("\nQR authentication timed out or canceled.");
    }
}
