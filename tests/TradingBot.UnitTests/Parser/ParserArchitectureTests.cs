using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Domain.Enums;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Exceptions;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Parsers;
using TradingBot.Parser.Pipeline;
using Xunit;

namespace TradingBot.UnitTests.Parser;

public class ParserArchitectureTests
{
    private readonly Mock<ILogger<SignalParserPipeline>> _pipelineLoggerMock = new();
    private readonly Mock<ILogger<DefaultSignalParser>> _parserLoggerMock = new();
    private readonly IOptions<ParserOptions> _defaultOptions = Options.Create(new ParserOptions());

    [Fact]
    public void ParserContext_ShouldCreate_WhenValidInputsProvided()
    {
        // Arrange
        var id = Guid.NewGuid();
        var message = "  BTCUSDT BUY Entry: 50000 \0  ";
        var source = "TelegramChannel1";
        var receivedAt = DateTime.UtcNow;
        var version = "1.0";

        // Act
        var context = new ParserContext(id, message, source, receivedAt, version);

        // Assert
        context.Should().NotBeNull();
        context.SignalId.Should().Be(id);
        context.RawMessage.Should().Be("BTCUSDT BUY Entry: 50000"); // Trimmed and null byte removed
        context.SourceChannel.Should().Be(source);
        context.ReceivedAt.Should().Be(receivedAt);
        context.ParserVersion.Should().Be(version);
    }

    [Theory]
    [InlineData("", "TelegramChannel1", "1.0", "RawMessage cannot be empty or whitespace.")]
    [InlineData("   ", "TelegramChannel1", "1.0", "RawMessage cannot be empty or whitespace.")]
    [InlineData("BTCUSDT BUY", "", "1.0", "SourceChannel cannot be null or empty.")]
    [InlineData("BTCUSDT BUY", "TelegramChannel1", "", "ParserVersion cannot be null or empty.")]
    public void ParserContext_ShouldThrowInvalidParserContextException_WhenInputsAreInvalid(
        string message, string source, string version, string expectedError)
    {
        // Arrange & Act
        Action act = () => new ParserContext(Guid.NewGuid(), message, source, DateTime.UtcNow, version);

        // Assert
        act.Should().Throw<InvalidParserContextException>().WithMessage(expectedError);
    }

    [Fact]
    public void ParserContext_ShouldThrowInvalidParserContextException_WhenSignalIdIsEmpty()
    {
        // Arrange & Act
        Action act = () => new ParserContext(Guid.Empty, "BTCUSDT BUY", "TelegramChannel1", DateTime.UtcNow, "1.0");

        // Assert
        act.Should().Throw<InvalidParserContextException>().WithMessage("SignalId cannot be empty.");
    }

    [Fact]
    public void ParserContext_ShouldThrowInvalidParserContextException_WhenRawMessageIsNull()
    {
        // Arrange & Act
        Action act = () => new ParserContext(Guid.NewGuid(), null!, "TelegramChannel1", DateTime.UtcNow, "1.0");

        // Assert
        act.Should().Throw<InvalidParserContextException>().WithMessage("RawMessage cannot be null.");
    }

    [Fact]
    public void ParserContext_ShouldThrowInvalidParserContextException_WhenRawMessageExceedsLimit()
    {
        // Arrange
        var message = new string('A', 101);

        // Act
        Action act = () => new ParserContext(Guid.NewGuid(), message, "TelegramChannel1", DateTime.UtcNow, "1.0", maxMessageLength: 100);

        // Assert
        act.Should().Throw<InvalidParserContextException>().WithMessage("*exceeds the maximum limit of 100 characters*");
    }

    [Fact]
    public void ParserResult_ShouldExposeProperties_WhenSuccessResultCreated()
    {
        // Arrange
        var signal = new ParsedSignal
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 50000m,
            StopLoss = 48000m,
            Leverage = 10,
            ConfidenceScore = 0.95
        };
        signal.TakeProfits.Add(55000m);
        var warnings = new[] { "Warning 1" };

        // Act
        var result = ParserResult.SuccessResult(signal, "1.0", warnings);

        // Assert
        result.Success.Should().BeTrue();
        result.ParsedSignal.Should().Be(signal);
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().Be("Warning 1");
        result.ParserVersion.Should().Be("1.0");
    }

    [Fact]
    public void ParserResult_ShouldExposeProperties_WhenFailureResultCreated()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2" };
        var warnings = new[] { "Warning 1" };

        // Act
        var result = ParserResult.Failure(errors, "1.0", warnings);

        // Assert
        result.Success.Should().BeFalse();
        result.ParsedSignal.Should().BeNull();
        result.Errors.Should().HaveCount(2).And.Contain(errors);
        result.Warnings.Should().ContainSingle().Which.Should().Be("Warning 1");
        result.ParserVersion.Should().Be("1.0");
    }

    [Fact]
    public async Task SignalParserPipeline_ShouldExecuteCorrectly_WithNoExtractors()
    {
        // Arrange
        var pipeline = new SignalParserPipeline(new List<ISignalExtractor>(), _defaultOptions, _pipelineLoggerMock.Object);
        var context = new ParserContext(Guid.NewGuid(), "BTCUSDT BUY", "TelegramChannel1", DateTime.UtcNow, "1.0");

        // Act
        var result = await pipeline.ExecuteAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Symbol.Should().BeNull();
        result.Side.Should().BeNull();
        result.EntryPrice.Should().BeNull();
    }

    [Fact]
    public async Task SignalParserPipeline_ShouldExecuteExtractorsInSequence()
    {
        // Arrange
        var extractor1Mock = new Mock<ISignalExtractor>();
        extractor1Mock.Setup(e => e.ExtractAsync(It.IsAny<ParserContext>(), It.IsAny<ParsedSignal>()))
            .Callback<ParserContext, ParsedSignal>((ctx, sig) => sig.Symbol = "ETHUSDT")
            .Returns(Task.CompletedTask);

        var extractor2Mock = new Mock<ISignalExtractor>();
        extractor2Mock.Setup(e => e.ExtractAsync(It.IsAny<ParserContext>(), It.IsAny<ParsedSignal>()))
            .Callback<ParserContext, ParsedSignal>((ctx, sig) => sig.Side = OrderSide.Sell)
            .Returns(Task.CompletedTask);

        var extractors = new List<ISignalExtractor> { extractor1Mock.Object, extractor2Mock.Object };
        var pipeline = new SignalParserPipeline(extractors, _defaultOptions, _pipelineLoggerMock.Object);
        var context = new ParserContext(Guid.NewGuid(), "ETHUSDT SELL", "TelegramChannel1", DateTime.UtcNow, "1.0");

        // Act
        var result = await pipeline.ExecuteAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Symbol.Should().Be("ETHUSDT");
        result.Side.Should().Be(OrderSide.Sell);

        extractor1Mock.Verify(e => e.ExtractAsync(context, It.IsAny<ParsedSignal>()), Times.Once);
        extractor2Mock.Verify(e => e.ExtractAsync(context, It.IsAny<ParsedSignal>()), Times.Once);
    }

    [Fact]
    public async Task SignalParserPipeline_ShouldThrowInvalidParserContextException_WhenContextNull()
    {
        // Arrange
        var pipeline = new SignalParserPipeline(new List<ISignalExtractor>(), _defaultOptions, _pipelineLoggerMock.Object);

        // Act
        Func<Task> act = async () => await pipeline.ExecuteAsync(null!);

        // Assert
        await act.Should().ThrowAsync<InvalidParserContextException>().WithMessage("Parser context cannot be null during execution.");
    }

    [Fact]
    public async Task SignalParserPipeline_ShouldThrowInvalidParserContextException_WhenMessageLengthExceedsConfiguredLimit()
    {
        // Arrange
        var options = Options.Create(new ParserOptions { MaxMessageLength = 5 });
        var pipeline = new SignalParserPipeline(new List<ISignalExtractor>(), options, _pipelineLoggerMock.Object);
        var context = new ParserContext(Guid.NewGuid(), "BTCUSDT BUY", "TelegramChannel1", DateTime.UtcNow, "1.0", maxMessageLength: 100);

        // Act
        Func<Task> act = async () => await pipeline.ExecuteAsync(context);

        // Assert
        await act.Should().ThrowAsync<InvalidParserContextException>().WithMessage("*exceeds maximum configured limit of 5 characters.*");
    }

    [Fact]
    public async Task SignalParserPipeline_ShouldTranslateUnexpectedExceptionToParserExecutionException()
    {
        // Arrange
        var extractorMock = new Mock<ISignalExtractor>();
        extractorMock.Setup(e => e.ExtractAsync(It.IsAny<ParserContext>(), It.IsAny<ParsedSignal>()))
            .ThrowsAsync(new InvalidOperationException("Simulated unexpected failure"));

        var extractors = new List<ISignalExtractor> { extractorMock.Object };
        var pipeline = new SignalParserPipeline(extractors, _defaultOptions, _pipelineLoggerMock.Object);
        var context = new ParserContext(Guid.NewGuid(), "BTCUSDT BUY", "TelegramChannel1", DateTime.UtcNow, "1.0");

        // Act
        Func<Task> act = async () => await pipeline.ExecuteAsync(context);

        // Assert
        var exceptionAssertion = await act.Should().ThrowAsync<ParserExecutionException>();
        exceptionAssertion.WithMessage("An error occurred while executing the parser pipeline.");
        exceptionAssertion.Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Simulated unexpected failure");
    }

    [Fact]
    public async Task DefaultSignalParser_ShouldReturnSuccess_WhenPipelineCompletesSuccessfully()
    {
        // Arrange
        var parsedSignal = new ParsedSignal { Symbol = "BTCUSDT", Side = OrderSide.Buy };
        var pipelineMock = new Mock<IParserPipeline>();
        pipelineMock.Setup(p => p.ExecuteAsync(It.IsAny<ParserContext>()))
            .ReturnsAsync(parsedSignal);

        var parser = new DefaultSignalParser(pipelineMock.Object, _defaultOptions, _parserLoggerMock.Object);
        var context = new ParserContext(Guid.NewGuid(), "BTCUSDT BUY", "TelegramChannel1", DateTime.UtcNow, "1.0");

        // Act
        var result = await parser.ParseAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ParsedSignal.Should().Be(parsedSignal);
        result.ParserVersion.Should().Be("1.0");
    }

    [Fact]
    public async Task DefaultSignalParser_ShouldReturnFailure_WhenPipelineThrowsParserException()
    {
        // Arrange
        var pipelineMock = new Mock<IParserPipeline>();
        pipelineMock.Setup(p => p.ExecuteAsync(It.IsAny<ParserContext>()))
            .ThrowsAsync(new InvalidParserContextException("Invalid pipeline context"));

        var parser = new DefaultSignalParser(pipelineMock.Object, _defaultOptions, _parserLoggerMock.Object);
        var context = new ParserContext(Guid.NewGuid(), "BTCUSDT BUY", "TelegramChannel1", DateTime.UtcNow, "1.0");

        // Act
        var result = await parser.ParseAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ParsedSignal.Should().BeNull();
        result.Errors.Should().ContainSingle().Which.Should().Be("Invalid pipeline context");
    }

    [Fact]
    public async Task DefaultSignalParser_ShouldReturnFailure_WhenPipelineThrowsUnexpectedException()
    {
        // Arrange
        var pipelineMock = new Mock<IParserPipeline>();
        pipelineMock.Setup(p => p.ExecuteAsync(It.IsAny<ParserContext>()))
            .ThrowsAsync(new NullReferenceException("Crashing unexpected"));

        var parser = new DefaultSignalParser(pipelineMock.Object, _defaultOptions, _parserLoggerMock.Object);
        var context = new ParserContext(Guid.NewGuid(), "BTCUSDT BUY", "TelegramChannel1", DateTime.UtcNow, "1.0");

        // Act
        var result = await parser.ParseAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ParsedSignal.Should().BeNull();
        result.Errors.Should().ContainSingle().Which.Should().Be("An unexpected error occurred during parsing.");
    }

    [Fact]
    public async Task DefaultSignalParser_ShouldReturnFailure_WhenContextIsNull()
    {
        // Arrange
        var pipelineMock = new Mock<IParserPipeline>();
        var parser = new DefaultSignalParser(pipelineMock.Object, _defaultOptions, _parserLoggerMock.Object);

        // Act
        var result = await parser.ParseAsync(null!);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Be("ParserContext cannot be null.");
    }
}
