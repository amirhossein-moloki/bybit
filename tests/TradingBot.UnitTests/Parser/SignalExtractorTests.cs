using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Domain.Enums;
using TradingBot.Parser;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Extractors;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Parsers;
using TradingBot.Parser.Pipeline;
using Xunit;

namespace TradingBot.UnitTests.Parser;

public class SignalExtractorTests
{
    private readonly Mock<ILogger<SignalParserPipeline>> _pipelineLoggerMock = new();
    private readonly Mock<ILogger<DefaultSignalParser>> _parserLoggerMock = new();
    private readonly IOptions<ParserOptions> _defaultOptions = Options.Create(new ParserOptions());

    [Theory]
    [InlineData("🔥 btc   long", "BTC LONG")]
    [InlineData("ETH\r\nSHORT", "ETH\nSHORT")]
    [InlineData("  SOL   -   USDT  ", "SOL - USDT")]
    [InlineData("🚀🚀 ADA LONG 🚀🚀", "ADA LONG")]
    public void Normalizer_ShouldCleanTextCorrectly(string input, string expected)
    {
        // Act
        var result = SignalTextNormalizer.Normalize(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("BTC LONG", "BTCUSDT")]
    [InlineData("ETH SHORT", "ETHUSDT")]
    [InlineData("🔥 BTC LONG", "BTCUSDT")]
    [InlineData("BTCUSDT", "BTCUSDT")]
    [InlineData("SOL-USDT", "SOLUSDT")]
    [InlineData("XRP/USDC", "XRPUSDT")]
    [InlineData("BNB_BUSD", "BNBUSDT")]
    public async Task SymbolExtractor_ShouldExtractAndNormalizeSymbol(string text, string expectedSymbol)
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), text, "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new SymbolExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.Symbol.Should().Be(expectedSymbol);
        signal.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("BTC LONG", OrderSide.Buy)]
    [InlineData("ETH SHORT", OrderSide.Sell)]
    [InlineData("BUY NOW", OrderSide.Buy)]
    [InlineData("SELL IMMEDIATELY", OrderSide.Sell)]
    [InlineData("LONG POSITION OPEN", OrderSide.Buy)]
    [InlineData("SHORT POSITION OPEN", OrderSide.Sell)]
    [InlineData("BULLISH SIGNALS", OrderSide.Buy)]
    [InlineData("BEARISH MOVEMENT", OrderSide.Sell)]
    public async Task DirectionExtractor_ShouldExtractDirectionCorrectly(string text, OrderSide expectedSide)
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), text, "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new DirectionExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.Side.Should().Be(expectedSide);
        signal.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Entry:60000", 60000)]
    [InlineData("Buy Zone:60000-60500", 60000)]
    [InlineData("Entry: 60,000", 60000)]
    [InlineData("Buy Zone: 1.234 - 1.250", 1.234)]
    public async Task EntryExtractor_ShouldExtractEntryPriceCorrectly(string text, decimal expectedPrice)
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), text, "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new EntryExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.EntryPrice.Should().Be(expectedPrice);
        signal.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task EntryExtractor_ShouldIgnoreEntryNowWithoutAddingError()
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), "Entry Now", "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new EntryExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.EntryPrice.Should().BeNull();
        signal.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task EntryExtractor_ShouldRecordErrorForInvalidFormat()
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), "Entry: ABC", "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new EntryExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.EntryPrice.Should().BeNull();
        signal.Errors.Should().ContainSingle().Which.Should().Be("Invalid entry price format");
    }

    [Theory]
    [InlineData("SL:59000", 59000)]
    [InlineData("Stop Loss:59000", 59000)]
    [InlineData("SL: 59,000.50", 59000.50)]
    public async Task StopLossExtractor_ShouldExtractStopLossCorrectly(string text, decimal expectedSl)
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), text, "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new StopLossExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.StopLoss.Should().Be(expectedSl);
        signal.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task StopLossExtractor_ShouldRecordErrorForInvalidFormat()
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), "SL: ABC", "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new StopLossExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.StopLoss.Should().BeNull();
        signal.Errors.Should().ContainSingle().Which.Should().Be("Invalid stop loss format");
    }

    [Fact]
    public async Task TakeProfitExtractor_ShouldExtractMultipleTargetsCorrectly()
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), "TP1:62000\nTP2:63000\nTarget:65000", "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new TakeProfitExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.TakeProfits.Should().HaveCount(3)
            .And.ContainInOrder(62000m, 63000m, 65000m);
        signal.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task TakeProfitExtractor_ShouldRemoveDuplicatesAndPreserveOrder()
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), "TP1: 62000\nTP2: 63000\nTP3: 62000", "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new TakeProfitExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.TakeProfits.Should().HaveCount(2)
            .And.ContainInOrder(62000m, 63000m);
        signal.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task TakeProfitExtractor_ShouldRecordErrorForInvalidFormat()
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), "TP1: ABC", "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new TakeProfitExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.TakeProfits.Should().BeEmpty();
        signal.Errors.Should().ContainSingle().Which.Should().Be("Invalid take profit format");
    }

    [Theory]
    [InlineData("20x", 20)]
    [InlineData("10X", 10)]
    [InlineData("Leverage:50", 50)]
    public async Task LeverageExtractor_ShouldExtractLeverageCorrectly(string text, int expectedLeverage)
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), text, "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new LeverageExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.Leverage.Should().Be(expectedLeverage);
        signal.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-5x")]
    [InlineData("Leverage: ABC")]
    public async Task LeverageExtractor_ShouldIgnoreInvalidLeverageFormats(string text)
    {
        // Arrange
        var context = new ParserContext(Guid.NewGuid(), text, "Channel", DateTime.UtcNow, "1.0");
        var signal = new ParsedSignal();
        var extractor = new LeverageExtractor();

        // Act
        await extractor.ExtractAsync(context, signal);

        // Assert
        signal.Leverage.Should().BeNull();
        signal.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Pipeline_ShouldExecuteExtractorsIndependentlyAndCollectErrors()
    {
        // Arrange
        var extractor1Mock = new Mock<ISignalExtractor>();
        extractor1Mock.Setup(e => e.ExtractAsync(It.IsAny<ParserContext>(), It.IsAny<ParsedSignal>()))
            .Callback<ParserContext, ParsedSignal>((ctx, sig) => sig.Symbol = "BTCUSDT")
            .Returns(Task.CompletedTask);

        // This extractor throws an unexpected exception
        var extractor2Mock = new Mock<ISignalExtractor>();
        extractor2Mock.Setup(e => e.ExtractAsync(It.IsAny<ParserContext>(), It.IsAny<ParsedSignal>()))
            .ThrowsAsync(new InvalidOperationException("Something crashed"));

        var extractor3Mock = new Mock<ISignalExtractor>();
        extractor3Mock.Setup(e => e.ExtractAsync(It.IsAny<ParserContext>(), It.IsAny<ParsedSignal>()))
            .Callback<ParserContext, ParsedSignal>((ctx, sig) => sig.Leverage = 10)
            .Returns(Task.CompletedTask);

        var extractors = new List<ISignalExtractor> { extractor1Mock.Object, extractor2Mock.Object, extractor3Mock.Object };
        var pipeline = new SignalParserPipeline(extractors, _defaultOptions, _pipelineLoggerMock.Object);
        var context = new ParserContext(Guid.NewGuid(), "BTC 10X", "Channel", DateTime.UtcNow, "1.0");

        // Act
        var signal = await pipeline.ExecuteAsync(context);

        // Assert
        signal.Should().NotBeNull();
        signal.Symbol.Should().Be("BTCUSDT");
        signal.Leverage.Should().Be(10);
        signal.Errors.Should().ContainSingle().Which.Should().Contain("Something crashed");
    }

    [Fact]
    public async Task FullParserIntegration_ShouldSuccessfullyParseAllFields_WhenValidSignalPassed()
    {
        // Arrange
        var text = @"
        🔥 BTC LONG
        Entry: 60,000.50
        SL: 59000
        TP1: 62000
        TP2: 63000
        Target: 65000
        20x
        ";
        var context = new ParserContext(Guid.NewGuid(), text, "Channel1", DateTime.UtcNow, "1.0");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddParser();
        var provider = services.BuildServiceProvider();

        var parser = provider.GetRequiredService<ISignalParser>();

        // Act
        var result = await parser.ParseAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();

        var signal = result.ParsedSignal;
        signal.Should().NotBeNull();
        signal!.Symbol.Should().Be("BTCUSDT");
        signal.Side.Should().Be(OrderSide.Buy);
        signal.EntryPrice.Should().Be(60000.50m);
        signal.StopLoss.Should().Be(59000m);
        signal.TakeProfits.Should().HaveCount(3).And.ContainInOrder(62000m, 63000m, 65000m);
        signal.Leverage.Should().Be(20);
    }

    [Fact]
    public async Task FullParserIntegration_ShouldReportWarningsForMissingOptionalFields()
    {
        // Arrange
        var text = "🔥 BTC LONG"; // Missing Entry, SL, TP, Leverage
        var context = new ParserContext(Guid.NewGuid(), text, "Channel1", DateTime.UtcNow, "1.0");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddParser();
        var provider = services.BuildServiceProvider();

        var parser = provider.GetRequiredService<ISignalParser>();

        // Act
        var result = await parser.ParseAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().HaveCount(4)
            .And.Contain("Entry not detected")
            .And.Contain("Stop loss not detected")
            .And.Contain("Take profits not detected")
            .And.Contain("Leverage not detected");

        var signal = result.ParsedSignal;
        signal.Should().NotBeNull();
        signal!.Symbol.Should().Be("BTCUSDT");
        signal.Side.Should().Be(OrderSide.Buy);
        signal.EntryPrice.Should().BeNull();
    }
}
