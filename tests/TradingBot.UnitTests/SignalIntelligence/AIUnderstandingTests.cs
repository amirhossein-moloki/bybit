using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Application.Repositories;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;
using TradingBot.Domain.Entities;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Events;
using TradingBot.Domain.SignalIntelligence.Interfaces;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Parsers;
using TradingBot.Parser.Services;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class AIUnderstandingTests
{
    private readonly Mock<IMessagePreprocessor> _mockPreprocessor = new();
    private readonly Mock<IMessageClassifier> _mockClassifier = new();
    private readonly Mock<IMessageAnalysisRepository> _mockAnalysisRepository = new();
    private readonly Mock<IMessageRepository> _mockMessageRepository = new();
    private readonly Mock<IIntelligenceEventPublisher> _mockEventPublisher = new();
    private readonly Mock<IUnitOfWork> _mockUnitOfWork = new();
    private readonly Mock<ILogger<MessageParser>> _mockParserLogger = new();
    private readonly Mock<ILogger<MockAIProvider>> _mockProviderLogger = new();
    private readonly Mock<ILogger<AIAnalyzer>> _mockAnalyzerLogger = new();
    private readonly Mock<IEventSanitizer> _mockSanitizer = new();
    private readonly Mock<ISignalContextRepository> _mockSignalContextRepository = new();
    private readonly Mock<ISignalRepository> _mockSignalRepository = new();

    public AIUnderstandingTests()
    {
        _mockPreprocessor.Setup(x => x.Preprocess(It.IsAny<string>())).Returns<string>(s => s);
        _mockSanitizer.Setup(x => x.Sanitize(It.IsAny<string>())).Returns<string>(s => s);
    }

    [Fact]
    public void AIDecisionEngine_ShouldSkipAI_WhenRuleBasedSignalIsCompleteAndHighConfidence()
    {
        // Arrange
        var engine = new AIDecisionEngine();
        var message = new TelegramMessage(1234, 5678, null, "BTCUSDT BUY Entry 60000 SL 59000", DateTime.UtcNow);
        var ruleBasedResult = new ParsedMessageResult
        {
            Type = MessageType.SIGNAL,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Entry = 60000m,
            StopLoss = 59000m,
            Confidence = 0.95m
        };

        // Act
        var decision = engine.DetermineAIUsage(message, ruleBasedResult);

        // Assert
        decision.ShouldUseAI.Should().BeFalse();
        decision.Reason.Should().Contain("successfully parsed");
    }

    [Theory]
    [InlineData(MessageType.UNKNOWN, 0.50, "UNKNOWN")]
    [InlineData(MessageType.SIGNAL, 0.60, "below the required threshold")]
    public void AIDecisionEngine_ShouldRequireAI_WhenConfidenceIsLowOrTypeIsUnknown(MessageType type, decimal confidence, string expectedReasonKeyword)
    {
        // Arrange
        var engine = new AIDecisionEngine();
        var message = new TelegramMessage(1234, 5678, null, "Ambiguous text", DateTime.UtcNow);
        var ruleBasedResult = new ParsedMessageResult
        {
            Type = type,
            Confidence = (decimal)confidence
        };

        // Act
        var decision = engine.DetermineAIUsage(message, ruleBasedResult);

        // Assert
        decision.ShouldUseAI.Should().BeTrue();
        decision.Reason.Should().Contain(expectedReasonKeyword);
    }

    [Fact]
    public void AIDecisionEngine_ShouldRequireAI_WhenCriticalFieldsAreMissingForSignal()
    {
        // Arrange
        var engine = new AIDecisionEngine();
        var message = new TelegramMessage(1234, 5678, null, "BTCUSDT BUY Entry 60000", DateTime.UtcNow);
        var ruleBasedResult = new ParsedMessageResult
        {
            Type = MessageType.SIGNAL,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            Entry = 60000m,
            StopLoss = null, // Missing stop loss
            Confidence = 0.85m
        };

        // Act
        var decision = engine.DetermineAIUsage(message, ruleBasedResult);

        // Assert
        decision.ShouldUseAI.Should().BeTrue();
        decision.Reason.Should().Contain("missing critical SIGNAL fields");
    }

    [Fact]
    public async Task PromptTemplateEngine_ShouldSupportVersioningAndMaskSensitiveData()
    {
        // Arrange
        _mockSanitizer.Setup(x => x.Sanitize(It.IsAny<string>())).Returns<string>(s => s.Replace("secret_password", "********"));
        var engine = new PromptTemplateEngine(_mockSanitizer.Object);

        // Act
        var prompt = engine.RenderPrompt("v1", "My secret_password is cool", "Previous context secret_password");

        // Assert
        prompt.Should().Contain("********");
        prompt.Should().NotContain("secret_password");
        prompt.Should().Contain("Classify message");
    }

    [Fact]
    public async Task AIAnalyzer_ShouldParseAndValidateValidJsonResponse()
    {
        // Arrange
        var options = Options.Create(new AIOptions { Provider = "Mock" });
        var mockProvider = new MockAIProvider(options, _mockProviderLogger.Object);
        MockAIProvider.Clear();
        MockAIProvider.EnqueueStubResponse("{\"type\":\"SIGNAL\",\"symbol\":\"BTCUSDT\",\"side\":\"Buy\",\"entry\":60000,\"stop_loss\":59000,\"take_profit\":[61000,62000],\"confidence\":0.95,\"reason\":\"Clear signal parsed\"}");

        var templateEngine = new PromptTemplateEngine(_mockSanitizer.Object);
        var analyzer = new AIAnalyzer(mockProvider, templateEngine, _mockEventPublisher.Object, _mockAnalyzerLogger.Object);
        var message = new TelegramMessage(1234, 5678, null, "BTC BUY Entry 60000 SL 59000", DateTime.UtcNow);

        // Act
        var result = await analyzer.AnalyzeMessageAsync(message, "context");

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be("SIGNAL");
        result.Symbol.Should().Be("BTCUSDT");
        result.Side.Should().Be("Buy");
        result.Entry.Should().Be(60000m);
        result.StopLoss.Should().Be(59000m);
        result.TakeProfits.Should().HaveCount(2);
        result.Confidence.Should().Be(0.95m);
        result.Reason.Should().Be("Clear signal parsed");
    }

    [Fact]
    public async Task AIAnalyzer_ShouldFailGracefully_WhenJsonResponseIsInvalidJson()
    {
        // Arrange
        var options = Options.Create(new AIOptions { Provider = "Mock" });
        var mockProvider = new MockAIProvider(options, _mockProviderLogger.Object);
        MockAIProvider.Clear();
        MockAIProvider.EnqueueStubResponse("Invalid JSON text response");

        var templateEngine = new PromptTemplateEngine(_mockSanitizer.Object);
        var analyzer = new AIAnalyzer(mockProvider, templateEngine, _mockEventPublisher.Object, _mockAnalyzerLogger.Object);
        var message = new TelegramMessage(1234, 5678, null, "Invalid", DateTime.UtcNow);

        // Act
        var result = await analyzer.AnalyzeMessageAsync(message, "context");

        // Assert
        result.Type.Should().Be("UNKNOWN");
        result.Confidence.Should().Be(0.0m);
        result.Reason.Should().Contain("invalid JSON");
        _mockEventPublisher.Verify(x => x.PublishAsync(It.IsAny<AIAnalysisFailed>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AIAnalyzer_ShouldFailGracefully_WhenTimeoutOrProviderFails()
    {
        // Arrange
        var options = Options.Create(new AIOptions { Provider = "Mock", TimeoutSeconds = 1, MaxRetries = 0 });
        var mockProvider = new MockAIProvider(options, _mockProviderLogger.Object);
        MockAIProvider.Clear();
        MockAIProvider.SimulateTimeout(true);

        var templateEngine = new PromptTemplateEngine(_mockSanitizer.Object);
        var analyzer = new AIAnalyzer(mockProvider, templateEngine, _mockEventPublisher.Object, _mockAnalyzerLogger.Object);
        var message = new TelegramMessage(1234, 5678, null, "Timeout text", DateTime.UtcNow);

        // Act
        var result = await analyzer.AnalyzeMessageAsync(message, "context");

        // Assert
        result.Type.Should().Be("UNKNOWN");
        result.Confidence.Should().Be(0.0m);
        result.Reason.Should().Contain("failed");
        _mockEventPublisher.Verify(x => x.PublishAsync(It.IsAny<AIAnalysisFailed>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ConversationContext_ShouldSortHistoryAndShowActiveSignals()
    {
        // Arrange
        var messages = new List<TelegramMessage>
        {
            new(100, 1, null, "Msg 1", DateTime.UtcNow.AddMinutes(-5)),
            new(100, 2, null, "Msg 2", DateTime.UtcNow.AddMinutes(-1)),
            new(100, 3, null, "Msg 3", DateTime.UtcNow.AddMinutes(-10))
        };
        var activeSignals = new List<SignalContext>
        {
            new(Guid.NewGuid(), 100, "BTCUSDT", SignalState.ACTIVE, "ACTIVE", 2)
        };

        var manager = new ConversationContextManager(_mockMessageRepository.Object, _mockSignalContextRepository.Object);
        var context = new ConversationContext
        {
            ChannelId = 100,
            Messages = messages.OrderBy(m => m.ReceivedAt).ToList(),
            ActiveSignals = activeSignals
        };

        // Act
        var summary = manager.GetContextSummary(context);

        // Assert
        summary.Should().Contain("Channel ID: 100");
        summary.Should().Contain("BTCUSDT");
        summary.Should().Contain("Msg 3"); // Earliest, should be in context
        summary.Should().Contain("Msg 2"); // Latest
    }

    [Fact]
    public void StateMachine_ShouldBlockInvalidTransitionsAndAllowValidTransitions()
    {
        // Arrange
        var signalId = Guid.NewGuid();
        var context = new SignalContext(signalId, 1234, "BTCUSDT", SignalState.RECEIVED, "None", 1);

        // 1. Valid transitions
        context.UpdateState(SignalState.ANALYZING, "Started Analysis", 2);
        context.CurrentState.Should().Be(SignalState.ANALYZING);

        context.UpdateState(SignalState.VALIDATED, "Validated", 3);
        context.CurrentState.Should().Be(SignalState.VALIDATED);

        context.UpdateState(SignalState.ACTIVE, "Activated", 4);
        context.CurrentState.Should().Be(SignalState.ACTIVE);

        context.UpdateState(SignalState.CLOSED, "Closed", 5);
        context.CurrentState.Should().Be(SignalState.CLOSED);

        // 2. Invalid backward transition (CLOSED is terminal)
        Action actBack = () => context.UpdateState(SignalState.ACTIVE, "Re-open", 6);
        actBack.Should().Throw<DomainException>().WithMessage("Cannot transition from terminal CLOSED state.");
    }

    [Fact]
    public async Task E2E_ThreeMessageWorkflowSimulation_ShouldProduceCorrectStructuredEventsAndStates()
    {
        // Arrange
        var channelId = 987654321;
        var messageId1 = 1001;
        var messageId2 = 1002;
        var messageId3 = 1003;

        var message1 = new TelegramMessage(channelId, messageId1, null, "EURUSD SELL Entry 1.1600 SL 1.1500", DateTime.UtcNow.AddMinutes(-5));
        var message2 = new TelegramMessage(channelId, messageId2, null, "فعال شد", DateTime.UtcNow.AddMinutes(-2));
        var message3 = new TelegramMessage(channelId, messageId3, null, "ریسک فری کنید", DateTime.UtcNow.AddMinutes(-1));

        var options = Options.Create(new AIOptions { Provider = "Mock" });
        var mockProvider = new MockAIProvider(options, _mockProviderLogger.Object);
        MockAIProvider.Clear();

        // Register expected stub responses for Message 2 and Message 3
        MockAIProvider.EnqueueStubResponse("{\"type\":\"TRADE_UPDATE\",\"action\":\"ACTIVATE_SIGNAL\",\"symbol\":\"EURUSD\",\"confidence\":0.95,\"reason\":\"Signal triggered/activated\"}");
        MockAIProvider.EnqueueStubResponse("{\"type\":\"TRADE_UPDATE\",\"action\":\"MOVE_STOP_TO_ENTRY\",\"symbol\":\"EURUSD\",\"confidence\":0.95,\"reason\":\"Move stop loss to entry\"}");

        var templateEngine = new PromptTemplateEngine(_mockSanitizer.Object);
        var analyzer = new AIAnalyzer(mockProvider, templateEngine, _mockEventPublisher.Object, _mockAnalyzerLogger.Object);
        var decisionEngine = new AIDecisionEngine();

        // In-memory repositories for integration mapping
        var signalsDb = new List<Signal>();
        var signalContextsDb = new List<SignalContext>();
        var messagesDb = new List<TelegramMessage> { message1, message2, message3 };
        var analysesDb = new List<MessageAnalysis>();

        _mockMessageRepository.Setup(x => x.GetRecentMessagesForChannelAsync(channelId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => messagesDb.OrderByDescending(m => m.ReceivedAt).ToList());

        _mockSignalContextRepository.Setup(x => x.GetActiveContextsForChannelAsync(channelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => signalContextsDb.Where(c => c.CurrentState != SignalState.CLOSED && c.CurrentState != SignalState.CANCELLED).ToList());

        _mockSignalContextRepository.Setup(x => x.GetActiveContextAsync(channelId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long cid, string sym, CancellationToken t) =>
                signalContextsDb.FirstOrDefault(c => c.ChannelId == cid && c.Symbol == sym && c.CurrentState != SignalState.CLOSED && c.CurrentState != SignalState.CANCELLED));

        _mockSignalContextRepository.Setup(x => x.GetLatestActiveContextAsync(channelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                signalContextsDb.Where(c => c.CurrentState != SignalState.CLOSED && c.CurrentState != SignalState.CANCELLED)
                                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt).FirstOrDefault());

        _mockSignalRepository.Setup(x => x.SaveAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()))
            .Callback<Signal, CancellationToken>((s, t) => signalsDb.Add(s));

        _mockSignalContextRepository.Setup(x => x.CreateAsync(It.IsAny<SignalContext>(), It.IsAny<CancellationToken>()))
            .Callback<SignalContext, CancellationToken>((c, t) => signalContextsDb.Add(c));

        _mockAnalysisRepository.Setup(x => x.CreateAsync(It.IsAny<MessageAnalysis>(), It.IsAny<CancellationToken>()))
            .Callback<MessageAnalysis, CancellationToken>((a, t) => analysesDb.Add(a));

        var contextManager = new ConversationContextManager(_mockMessageRepository.Object, _mockSignalContextRepository.Object);

        var messageClassifier = new TradingBot.Application.SignalIntelligence.Parser.MessageClassifier(_mockPreprocessor.Object);

        var parser = new MessageParser(
            _mockPreprocessor.Object,
            messageClassifier,
            _mockAnalysisRepository.Object,
            _mockMessageRepository.Object,
            _mockEventPublisher.Object,
            _mockUnitOfWork.Object,
            _mockParserLogger.Object,
            analyzer,
            decisionEngine,
            contextManager,
            _mockSignalContextRepository.Object,
            _mockSignalRepository.Object
        );

        // --- STEP 1: Process Message 1 (Structured Signal) ---
        var result1 = await parser.ParseAsync(message1);

        result1.Type.Should().Be(MessageType.SIGNAL);
        result1.Symbol.Should().Be("EURUSD");
        result1.Side.Should().Be(OrderSide.Sell);
        result1.Entry.Should().Be(1.1600m);
        result1.StopLoss.Should().Be(1.1500m);
        result1.Source.Should().Be(ParserSource.RULE_BASED); // Rule based because complete

        signalsDb.Should().ContainSingle(s => s.Symbol == "EURUSD");
        signalContextsDb.Should().ContainSingle(c => c.Symbol == "EURUSD" && c.CurrentState == SignalState.VALIDATED);

        // --- STEP 2: Process Message 2 (Ambiguous Activation "فعال شد") ---
        var result2 = await parser.ParseAsync(message2);

        result2.Type.Should().Be(MessageType.TRADE_UPDATE);
        result2.Source.Should().Be(ParserSource.AI); // Handled by AI because ambiguous
        result2.Confidence.Should().Be(0.95m);

        // Verify state machine transitioned to ACTIVE
        signalContextsDb.Should().ContainSingle(c => c.Symbol == "EURUSD" && c.CurrentState == SignalState.ACTIVE);

        // Verify ContextResolved and SignalStateChanged events were published
        _mockEventPublisher.Verify(x => x.PublishAsync(It.IsAny<ContextResolved>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockEventPublisher.Verify(x => x.PublishAsync(It.IsAny<SignalStateChanged>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // --- STEP 3: Process Message 3 (Ambiguous "ریسک فری کنید") ---
        var result3 = await parser.ParseAsync(message3);

        result3.Type.Should().Be(MessageType.TRADE_UPDATE);
        result3.Source.Should().Be(ParserSource.AI); // Handled by AI
        result3.Confidence.Should().Be(0.95m);

        // Verify state machine transitioned to RISK_FREE
        signalContextsDb.Should().ContainSingle(c => c.Symbol == "EURUSD" && c.CurrentState == SignalState.RISK_FREE);

        // Ensure Unit of Work successfully requested saves
        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(3));
    }
}
