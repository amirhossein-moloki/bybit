using System;
using FluentAssertions;
using TradingBot.Application.Interfaces;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Exceptions;
using TradingBot.Infrastructure.Configuration;
using TradingBot.Infrastructure.Security;
using Xunit;

namespace TradingBot.UnitTests;

public class DatabaseSecurityAndPerformanceTests
{
    [Fact]
    public void EncryptionService_ShouldEncryptAndDecryptCorrectly_WhenUsingValidKey()
    {
        // Arrange
        var settings = new TradingBotSettings
        {
            Security = new SecuritySettings
            {
                EncryptionKey = "MySuperSecretKeyForTradingApplication123!"
            }
        };
        var service = new EncryptionService(settings);
        var originalText = "BybitSuperSecretAPIKeyTextValue";

        // Act
        var encrypted = service.Encrypt(originalText);
        var decrypted = service.Decrypt(encrypted);

        // Assert
        encrypted.Should().NotBeNullOrEmpty();
        encrypted.Should().NotBe(originalText);
        decrypted.Should().Be(originalText);
    }

    [Fact]
    public void EncryptionService_ShouldEncryptAndDecryptCorrectly_WhenUsingFallbackKey()
    {
        // Arrange
        var settings = new TradingBotSettings
        {
            Security = new SecuritySettings
            {
                EncryptionKey = "" // Empty key to trigger fallback
            }
        };
        var service = new EncryptionService(settings);
        var originalText = "FallbackKeyCheckPlaintext";

        // Act
        var encrypted = service.Encrypt(originalText);
        var decrypted = service.Decrypt(encrypted);

        // Assert
        encrypted.Should().NotBeNullOrEmpty();
        decrypted.Should().Be(originalText);
    }

    [Theory]
    [InlineData("My api_key is apiKey123", "My [REDACTED] is apiKey123")]
    [InlineData("Save password of user!", "Save [REDACTED] of user!")]
    [InlineData("No secrets here, but secret_key: my-secret-token is present.", "No secrets here, but [REDACTED]: [REDACTED] is present.")]
    public void SystemLog_CreateAuditLog_ShouldSanitizeSensitiveFields(string originalDesc, string expectedDesc)
    {
        // Act
        var log = SystemLog.CreateAuditLog("Information", "UserLogin", "User", "USR-100", originalDesc);

        // Assert
        log.Message.Should().Contain("[Audit]");
        log.Message.Should().Contain("Op: UserLogin");
        log.Message.Should().Contain("Entity: User (USR-100)");
        log.Message.Should().Contain(expectedDesc);
        log.Message.Should().NotContain("my-secret-token");
    }

    [Fact]
    public void SystemLog_CreateAuditLog_ShouldThrowException_WhenRequiredArgumentsAreEmpty()
    {
        // Act & Assert
        FluentActions.Invoking(() => SystemLog.CreateAuditLog("Info", "", "User", "1", "Desc"))
            .Should().Throw<DomainException>();

        FluentActions.Invoking(() => SystemLog.CreateAuditLog("Info", "Op", "", "1", "Desc"))
            .Should().Throw<DomainException>();

        FluentActions.Invoking(() => SystemLog.CreateAuditLog("Info", "Op", "User", "", "Desc"))
            .Should().Throw<DomainException>();
    }
}
