using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Options;
using TradingBot.Application.SignalIntelligence.Configuration;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Application.SignalIntelligence.Validation;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Enums;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class SignalValidationTests
{
    private readonly SignalValidationService _service;
    private readonly SignalIntelligenceOptions _options;

    public SignalValidationTests()
    {
        _options = new SignalIntelligenceOptions
        {
            MinimumConfidence = 0.85m
        };
        var optionsMock = Options.Create(_options);
        _service = new SignalValidationService(optionsMock);
    }

    [Fact]
    public void Validate_WithValidSignal_ShouldAccept()
    {
        // Arrange
        var result = new ParsedMessageResult
        {
            Type = MessageType.SIGNAL,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Entry = 45000m,
            StopLoss = 44000m,
            TakeProfits = new List<decimal> { 46000m, 47000m },
            Confidence = 0.90m
        };

        // Act
        var validation = _service.Validate(result);

        // Assert
        validation.IsValid.Should().BeTrue();
        validation.ValidationStatus.Should().Be("ACCEPT");
        validation.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithMissingSymbol_ShouldReject()
    {
        // Arrange
        var result = new ParsedMessageResult
        {
            Type = MessageType.SIGNAL,
            Symbol = "",
            Side = OrderSide.Buy,
            Entry = 45000m,
            Confidence = 0.90m
        };

        // Act
        var validation = _service.Validate(result);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.ValidationStatus.Should().Be("REJECT");
        validation.Errors.Should().Contain(e => e.Contains("Symbol is required"));
    }

    [Fact]
    public void Validate_WithInvalidSide_ShouldReject()
    {
        // Arrange
        var result = new ParsedMessageResult
        {
            Type = MessageType.SIGNAL,
            Symbol = "BTCUSDT",
            Side = null,
            Entry = 45000m,
            Confidence = 0.90m
        };

        // Act
        var validation = _service.Validate(result);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.ValidationStatus.Should().Be("REJECT");
        validation.Errors.Should().Contain(e => e.Contains("Valid Side is required"));
    }

    [Fact]
    public void Validate_WithInvalidEntryPrice_ShouldReject()
    {
        // Arrange
        var result = new ParsedMessageResult
        {
            Type = MessageType.SIGNAL,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Entry = -10m,
            Confidence = 0.90m
        };

        // Act
        var validation = _service.Validate(result);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.ValidationStatus.Should().Be("REJECT");
        validation.Errors.Should().Contain(e => e.Contains("Valid Entry price is required"));
    }

    [Fact]
    public void Validate_WithLowConfidence_ShouldReturnReviewOrReject()
    {
        // Arrange
        var result = new ParsedMessageResult
        {
            Type = MessageType.SIGNAL,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Entry = 45000m,
            Confidence = 0.80m // below 0.85
        };

        // Act
        var validation = _service.Validate(result);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.ValidationStatus.Should().Be("REVIEW_REQUIRED");
        validation.Errors.Should().Contain(e => e.Contains("Confidence score"));
    }

    [Fact]
    public void Validate_WithInvalidActionForUpdate_ShouldReject()
    {
        // Arrange
        var result = new ParsedMessageResult
        {
            Type = MessageType.TRADE_UPDATE,
            Action = (TradeAction)999, // Unknown enum value
            Confidence = 0.90m
        };

        // Act
        var validation = _service.Validate(result);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.ValidationStatus.Should().Be("REJECT");
        validation.Errors.Should().Contain(e => e.Contains("Unknown or invalid action"));
    }
}
