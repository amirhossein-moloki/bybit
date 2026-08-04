using System;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using TradingBot.Exchange.Bybit.Services;
using Xunit;

namespace TradingBot.UnitTests;

public class BybitSignatureTests
{
    [Fact]
    public void GenerateSignature_ShouldGenerateCorrectHmacSha256Signature_ForStandardParameters()
    {
        // Arrange
        var apiSecret = "my_api_secret_key";
        var apiKey = "my_api_key";
        var timestamp = "1672211928338";
        var recvWindow = "5000";
        var payload = "{\"category\":\"spot\",\"symbol\":\"BTCUSDT\",\"side\":\"Buy\",\"orderType\":\"Limit\",\"qty\":\"0.01\",\"price\":\"28000\"}";

        // Compute expected signature manually in the test
        var rawData = $"{timestamp}{apiKey}{recvWindow}{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        var expectedSignature = BitConverter.ToString(expectedBytes).Replace("-", "").ToLower();

        // Act
        var actualSignature = BybitSignatureGenerator.GenerateSignature(apiSecret, apiKey, timestamp, recvWindow, payload);

        // Assert
        actualSignature.Should().Be(expectedSignature);
    }

    [Fact]
    public void GenerateSignature_ShouldThrowException_WhenSecretIsNull()
    {
        // Arrange
        Action act = () => BybitSignatureGenerator.GenerateSignature(null!, "key", "123", "5000", "payload");

        // Act & Assert
        act.Should().Throw<ArgumentException>();
    }
}
