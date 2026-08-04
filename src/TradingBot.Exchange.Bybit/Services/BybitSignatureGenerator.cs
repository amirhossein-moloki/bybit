using System;
using System.Security.Cryptography;
using System.Text;

namespace TradingBot.Exchange.Bybit.Services;

public static class BybitSignatureGenerator
{
    public static string GenerateSignature(
        string apiSecret,
        string apiKey,
        string timestamp,
        string recvWindow,
        string payload)
    {
        if (string.IsNullOrEmpty(apiSecret))
            throw new ArgumentException("API Secret cannot be null or empty.", nameof(apiSecret));

        // For Bybit v5: timestamp + apiKey + recvWindow + payload
        var rawData = $"{timestamp}{apiKey}{recvWindow}{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawData));

        return BitConverter.ToString(signatureBytes).Replace("-", "").ToLower();
    }
}
