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

        if (qrAuthService == null || authService == null)
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
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Warning checking status: {ex.Message}");
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
            await RunOtpFlowAsync(authService, input, output);
        }

        await output.WriteLineAsync("==========================================================");
    }

    private static async Task RunOtpFlowAsync(ITelegramAuthenticationService authService, TextReader input, TextWriter output)
    {
        await output.WriteLineAsync("\n--- OTP Login Flow ---");
        await output.WriteAsync("Enter phone number with country code (e.g. +989123456789): ");
        var phoneNumber = (await input.ReadLineAsync())?.Trim();

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            await output.WriteLineAsync("Error: Phone number cannot be empty.");
            return;
        }

        await output.WriteLineAsync($"Sending verification code to {phoneNumber}...");
        var startResult = await authService.StartOtpLoginAsync(phoneNumber);

        if (!startResult.Success)
        {
            await output.WriteLineAsync($"Failed to send code: {startResult.Error}");
            return;
        }

        if (startResult.PhoneCodeHash == "authenticated")
        {
            await output.WriteLineAsync("Successfully authenticated!");
            return;
        }

        await output.WriteAsync("Enter the verification code received via Telegram/SMS: ");
        var code = (await input.ReadLineAsync())?.Trim();

        if (string.IsNullOrWhiteSpace(code))
        {
            await output.WriteLineAsync("Error: Verification code cannot be empty.");
            return;
        }

        await output.WriteLineAsync("Verifying code...");
        var verifyResult = await authService.VerifyOtpAsync(phoneNumber, startResult.PhoneCodeHash ?? "sent", code);

        if (verifyResult.Success)
        {
            await output.WriteLineAsync("Telegram authenticated successfully!");
            return;
        }

        if (verifyResult.RequiresPassword)
        {
            await output.WriteLineAsync("Two-Factor Authentication (2FA) Password Required!");
            await output.WriteAsync("Enter your Telegram 2FA password: ");
            var password = (await input.ReadLineAsync())?.Trim();

            if (string.IsNullOrWhiteSpace(password))
            {
                await output.WriteLineAsync("Error: Password cannot be empty.");
                return;
            }

            await output.WriteLineAsync("Verifying 2FA password...");
            var passResult = await authService.VerifyPasswordAsync(password);

            if (passResult.Success)
            {
                await output.WriteLineAsync("Telegram 2FA authentication successful! Session connected.");
            }
            else
            {
                await output.WriteLineAsync($"2FA Authentication Failed: {passResult.Error}");
            }
            return;
        }

        await output.WriteLineAsync($"Verification Failed: {verifyResult.Error}");
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
