using System;
using FluentAssertions;
using TradingBot.Application.Monitoring.Services;
using Xunit;

namespace TradingBot.UnitTests.Monitoring;

public class EventSanitizerTests
{
    private readonly EventSanitizer _sanitizer = new();

    [Theory]
    [InlineData("api_key: abcdef12345", "[REDACTED]: [REDACTED]")]
    [InlineData("api_key=abcdef12345", "[REDACTED]=[REDACTED]")]
    [InlineData("secret_key: super_secret_val", "[REDACTED]: [REDACTED]")]
    [InlineData("password: MyPassword123", "[REDACTED]: [REDACTED]")]
    [InlineData("token: 123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11", "[REDACTED]: [REDACTED]")]
    [InlineData("bearer testtoken", "[REDACTED] [REDACTED]")]
    [InlineData("authorization: bearer testtoken", "[REDACTED]: [REDACTED] [REDACTED]")]
    public void Sanitize_ShouldRedactSensitiveCredentials(string input, string expected)
    {
        // Act
        var result = _sanitizer.Sanitize(input);

        // Assert
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void SanitizeAndLimit_ShouldTruncateOversizedPayloads()
    {
        // Arrange
        var hugePayload = new string('A', 500);

        // Act
        var result = _sanitizer.SanitizeAndLimit(hugePayload, 100);

        // Assert
        result.Should().HaveLength(115); // 100 chars + "... [TRUNCATED]" (length of "... [TRUNCATED]" is 15)
        result.Should().EndWith("... [TRUNCATED]");
    }

    [Fact]
    public void SanitizeAndLimit_ShouldNotTruncateWhenUnderLimit()
    {
        // Arrange
        var normalPayload = "Normal payload under limit";

        // Act
        var result = _sanitizer.SanitizeAndLimit(normalPayload, 100);

        // Assert
        result.Should().Be(normalPayload);
    }
}
