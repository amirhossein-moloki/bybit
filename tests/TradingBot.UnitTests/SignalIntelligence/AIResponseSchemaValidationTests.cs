using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using TradingBot.Application.SignalIntelligence.Configuration;
using TradingBot.Application.SignalIntelligence.Validation;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class AIResponseSchemaValidationTests
{
    private readonly SignalValidationService _service;

    public AIResponseSchemaValidationTests()
    {
        var options = new SignalIntelligenceOptions();
        var optionsMock = Options.Create(options);
        _service = new SignalValidationService(optionsMock);
    }

    [Fact]
    public void ValidateAIResponse_WithValidSignalJson_ShouldAccept()
    {
        // Arrange
        var json = "{\"type\":\"SIGNAL\",\"symbol\":\"BTCUSDT\",\"side\":\"Buy\",\"entry\":45000,\"confidence\":0.90}";

        // Act
        var result = _service.ValidateAIResponse(json);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidationStatus.Should().Be("ACCEPT");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateAIResponse_WithInvalidJsonSyntax_ShouldReject()
    {
        // Arrange
        var json = "{\"type\":\"SIGNAL\", \"symbol\": \"BTCUSDT\" "; // Missing closing brace

        // Act
        var result = _service.ValidateAIResponse(json);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationStatus.Should().Be("REJECT");
        result.Errors.Should().Contain(e => e.Contains("Invalid JSON syntax"));
    }

    [Fact]
    public void ValidateAIResponse_WithMissingType_ShouldReject()
    {
        // Arrange
        var json = "{\"symbol\":\"BTCUSDT\",\"side\":\"Buy\",\"entry\":45000}";

        // Act
        var result = _service.ValidateAIResponse(json);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationStatus.Should().Be("REJECT");
        result.Errors.Should().Contain(e => e.Contains("Missing required field: type"));
    }

    [Fact]
    public void ValidateAIResponse_WithUnknownMessageType_ShouldReject()
    {
        // Arrange
        var json = "{\"type\":\"OPEN_TRADE_NOW\"}";

        // Act
        var result = _service.ValidateAIResponse(json);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationStatus.Should().Be("REJECT");
        result.Errors.Should().Contain(e => e.Contains("Invalid MessageType"));
    }
}
