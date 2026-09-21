using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Models;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Application.SignalIntelligence.Parser;
using TradingBot.Application.Services;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Parsers;
using TradingBot.Parser.Services;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class MessageReprocessingTests
{
    private readonly Mock<IMessageRepository> _mockMsgRepo;
    private readonly Mock<IMessageProcessingAttemptRepository> _mockAttemptRepo;
    private readonly Mock<IMessageAnalysisRepository> _mockAnalysisRepo;
    private readonly Mock<ISignalExtractionRepository> _mockExtractionRepo;
    private readonly Mock<IMessageContextBuilder> _mockContextBuilder;
    private readonly Mock<IDeterministicRuleEngine> _mockRuleEngine;
    private readonly Mock<ITargetResolver> _mockTargetResolver;
    private readonly Mock<IIntentSafetyValidator> _mockSafetyValidator;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ILogger<MessageReprocessingService>> _mockLogger;

    private readonly StructuredSignalExtractor _structuredExtractor;
    private readonly MessageClassifier _messageClassifier;
    private readonly List<MessageProcessingAttempt> _attemptsList;

    public MessageReprocessingTests()
    {
        _mockMsgRepo = new Mock<IMessageRepository>();
        _mockAttemptRepo = new Mock<IMessageProcessingAttemptRepository>();
        _mockAnalysisRepo = new Mock<IMessageAnalysisRepository>();
        _mockExtractionRepo = new Mock<ISignalExtractionRepository>();
        _mockContextBuilder = new Mock<IMessageContextBuilder>();
        _mockRuleEngine = new Mock<IDeterministicRuleEngine>();
        _mockTargetResolver = new Mock<ITargetResolver>();
        _mockSafetyValidator = new Mock<IIntentSafetyValidator>();
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockLogger = new Mock<ILogger<MessageReprocessingService>>();

        _attemptsList = new List<MessageProcessingAttempt>();

        // Real parser classes
        var preprocessor = new MessagePreprocessor();
        _messageClassifier = new MessageClassifier(preprocessor);

        var options = Microsoft.Extensions.Options.Options.Create(new ExtractionRulesOptions());
        var mockPublisher = new Mock<IIntelligenceEventPublisher>();
        var mockExtractorRepo = new Mock<ISignalExtractionRepository>();
        var mockLoggerExtractor = new Mock<ILogger<StructuredSignalExtractor>>();

        _structuredExtractor = new StructuredSignalExtractor(
            mockExtractorRepo.Object,
            mockPublisher.Object,
            _mockUnitOfWork.Object,
            options,
            mockLoggerExtractor.Object
        );

        // Default mock setups
        _mockAttemptRepo
            .Setup(r => r.GetByTelegramMessageIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid msgId, CancellationToken _) => _attemptsList.Where(a => a.TelegramMessageId == msgId).ToList());

        _mockAttemptRepo
            .Setup(r => r.CreateAsync(It.IsAny<MessageProcessingAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<MessageProcessingAttempt, CancellationToken>((a, _) => _attemptsList.Add(a))
            .Returns(Task.CompletedTask);

        _mockAttemptRepo
            .Setup(r => r.UpdateAsync(It.IsAny<MessageProcessingAttempt>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockContextBuilder
            .Setup(c => c.BuildContextAsync(It.IsAny<TelegramMessageDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TelegramMessageDto dto, CancellationToken _) => new MessageIntelligenceContext
            {
                CurrentMessage = new CurrentMessageContext
                {
                    MessageId = dto.MessageId,
                    ChannelId = dto.ChannelId,
                    Text = dto.Text,
                    Date = dto.Date
                }
            });

        _mockTargetResolver
            .Setup(t => t.ResolveTargetsAsync(It.IsAny<StructuredIntent>(), It.IsAny<MessageIntelligenceContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StructuredIntent intent, MessageIntelligenceContext _, CancellationToken __) => intent);
    }

    [Fact]
    public async Task ReprocessMessage_Message48283_ShouldReprocessIntoCorrectEntrySignal_WhilePreservingHistoricalAttempt1()
    {
        // Arrange: Message #48283 text
        string rawText = @"💎 سیگنال
📊 جفت ارز: GBP/USD
📉 نوع معامله: خرید ( BUY)
📍 نقطه ورود: 1.33530
🎯 حد سود
تارگت اول: 1.33720
تارگت دوم: 1.33980
تارگت سوم: 1.34390
🛑 حد ضرر (Stop Loss) 1.33340
نهایتا 2 پیپ اسپرد صرفا روی نقطه ورود لحاظ گردد";

        var messageId = Guid.NewGuid();
        var message = new TelegramMessage(
            channelId: -1001492324861,
            messageId: 48283,
            senderId: 10001,
            content: rawText,
            receivedAt: DateTime.UtcNow
        );

        _mockMsgRepo.Setup(r => r.GetByIdAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

        // Historical Attempt #1 produced incorrect TakePartialProfit
        var historicalAttempt1 = new MessageProcessingAttempt(
            telegramMessageId: messageId,
            attemptNumber: 1,
            triggerType: "Automatic",
            triggeredBy: "System",
            processingMode: "REPROCESS_AND_EVALUATE",
            parserVersion: "v1.0-Legacy"
        );
        historicalAttempt1.Complete(
            intent: "PositionManagement",
            symbol: "GBPUSD",
            side: "TakePartialProfit",
            entryPrice: 0m,
            stopLoss: null,
            takeProfitsJson: null,
            leverage: null,
            spreadAllowance: null,
            validationResult: "Invalid",
            riskResult: "No Active Position Found",
            executionResult: "NoOrder"
        );
        _attemptsList.Add(historicalAttempt1);

        var service = new MessageReprocessingService(
            _mockMsgRepo.Object,
            _mockAttemptRepo.Object,
            _messageClassifier,
            _structuredExtractor,
            _mockContextBuilder.Object,
            _mockRuleEngine.Object,
            _mockTargetResolver.Object,
            _mockSafetyValidator.Object,
            _mockUnitOfWork.Object,
            _mockLogger.Object,
            _mockAnalysisRepo.Object,
            _mockExtractionRepo.Object
        );

        // Act
        var result = await service.ReprocessMessageAsync(messageId, new ReprocessMessageRequestDto("REPROCESS_ONLY"));

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Attempt);

        // 1. Original message remains unchanged
        Assert.Equal(rawText, message.Content);
        Assert.Equal(48283, message.MessageId);

        // 2. Attempt #1 remains intact (Historical evidence preserved)
        Assert.Equal(1, historicalAttempt1.AttemptNumber);
        Assert.Equal("TakePartialProfit", historicalAttempt1.Side);
        Assert.Equal("No Active Position Found", historicalAttempt1.RiskResult);

        // 3. New Attempt #2 correctly identifies EntrySignal parameters
        var newAttempt = result.Attempt;
        Assert.Equal(2, newAttempt.AttemptNumber);
        Assert.Equal("EntrySignal", newAttempt.Intent);
        Assert.Equal("GBPUSD", newAttempt.Symbol);
        Assert.Equal("BUY", newAttempt.Side);
        Assert.Equal(1.33530m, newAttempt.EntryPrice);
        Assert.Equal(1.33340m, newAttempt.StopLoss);
        Assert.NotNull(newAttempt.TakeProfitsJson);
        Assert.Contains("1.3372", newAttempt.TakeProfitsJson);
        Assert.Contains("1.3398", newAttempt.TakeProfitsJson);
        Assert.Contains("1.3439", newAttempt.TakeProfitsJson);
        Assert.Equal("2 pips", newAttempt.SpreadAllowance);
        Assert.Equal("Valid", newAttempt.ValidationResult);

        // 4. CRITICAL SAFETY: Reprocess alone NEVER places an order
        Assert.Equal("NoOrder", newAttempt.ExecutionResult);
    }

    [Fact]
    public async Task ReprocessMessage_ReprocessAlone_ShouldNeverCreateExchangeOrder()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TelegramMessage(1001, 500, 200, "BUY BTCUSDT @ 60000 SL 59000 TP 62000", DateTime.UtcNow);

        _mockMsgRepo.Setup(r => r.GetByIdAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

        var service = new MessageReprocessingService(
            _mockMsgRepo.Object,
            _mockAttemptRepo.Object,
            _messageClassifier,
            _structuredExtractor,
            _mockContextBuilder.Object,
            _mockRuleEngine.Object,
            _mockTargetResolver.Object,
            _mockSafetyValidator.Object,
            _mockUnitOfWork.Object,
            _mockLogger.Object
        );

        // Act
        var result = await service.ReprocessMessageAsync(messageId, new ReprocessMessageRequestDto());

        // Assert
        Assert.True(result.Success);
        Assert.Equal("NoOrder", result.Attempt.ExecutionResult);
    }

    [Fact]
    public async Task ReprocessMessage_RapidDuplicateClicks_ShouldPreventConcurrentAttempt()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TelegramMessage(1001, 500, 200, "BUY BTCUSDT @ 60000", DateTime.UtcNow);

        _mockMsgRepo.Setup(r => r.GetByIdAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

        // Active attempt already in progress
        var activeAttempt = new MessageProcessingAttempt(message.Id, 1, "ManualReprocess");
        _attemptsList.Add(activeAttempt);

        var service = new MessageReprocessingService(
            _mockMsgRepo.Object,
            _mockAttemptRepo.Object,
            _messageClassifier,
            _structuredExtractor,
            _mockContextBuilder.Object,
            _mockRuleEngine.Object,
            _mockTargetResolver.Object,
            _mockSafetyValidator.Object,
            _mockUnitOfWork.Object,
            _mockLogger.Object
        );

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReprocessMessageAsync(messageId, new ReprocessMessageRequestDto())
        );
    }

    [Fact]
    public async Task ReprocessMessage_ExpiredSignal_InEvaluateMode_ShouldMarkRiskResultAsExpired()
    {
        // Arrange: Signal received 2 hours ago
        var messageId = Guid.NewGuid();
        var oldMessage = new TelegramMessage(1001, 500, 200, "BUY BTCUSDT @ 60000 SL 59000", DateTime.UtcNow.AddHours(-2));

        _mockMsgRepo.Setup(r => r.GetByIdAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldMessage);

        var service = new MessageReprocessingService(
            _mockMsgRepo.Object,
            _mockAttemptRepo.Object,
            _messageClassifier,
            _structuredExtractor,
            _mockContextBuilder.Object,
            _mockRuleEngine.Object,
            _mockTargetResolver.Object,
            _mockSafetyValidator.Object,
            _mockUnitOfWork.Object,
            _mockLogger.Object
        );

        // Act
        var result = await service.ReprocessMessageAsync(messageId, new ReprocessMessageRequestDto("REPROCESS_AND_EVALUATE"));

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Expired", result.Attempt.RiskResult);
        Assert.Equal("Expired", result.Attempt.Status);
        Assert.Equal("NoOrder", result.Attempt.ExecutionResult);
    }

    [Fact]
    public async Task ExecuteAttemptTrade_WhenExplicitlyConfirmed_ShouldExecuteTrade()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var message = new TelegramMessage(1001, 500, 200, "BUY BTCUSDT @ 60000 SL 59000", DateTime.UtcNow);

        var completedAttempt = new MessageProcessingAttempt(messageId, 2, "ManualReprocess");
        completedAttempt.Complete(
            intent: "EntrySignal",
            symbol: "BTCUSDT",
            side: "BUY",
            entryPrice: 60000m,
            stopLoss: 59000m,
            takeProfitsJson: "[]",
            leverage: 10m,
            spreadAllowance: null,
            validationResult: "Valid",
            riskResult: "Passed",
            executionResult: "NoOrder"
        );

        _mockMsgRepo.Setup(r => r.GetByIdAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

        _mockAttemptRepo.Setup(r => r.GetByIdAsync(attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(completedAttempt);

        var service = new MessageReprocessingService(
            _mockMsgRepo.Object,
            _mockAttemptRepo.Object,
            _messageClassifier,
            _structuredExtractor,
            _mockContextBuilder.Object,
            _mockRuleEngine.Object,
            _mockTargetResolver.Object,
            _mockSafetyValidator.Object,
            _mockUnitOfWork.Object,
            _mockLogger.Object
        );

        // Act
        var execResult = await service.ExecuteAttemptTradeAsync(
            messageId,
            attemptId,
            new ExplicitExecutionRequestDto(attemptId, ConfirmedBy: "Operator (Dashboard)")
        );

        // Assert
        Assert.True(execResult.Success);
        Assert.Equal("Executed", execResult.Status);
        Assert.NotNull(execResult.OrderId);
        Assert.Equal("ExecutedByOperator", completedAttempt.ExecutionResult);
    }

    [Fact]
    public void DependencyInjection_ShouldResolveMessageReprocessingService_WhenDIContainerBuilt()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        services.AddLogging();
        services.AddApplication(configuration);
        services.AddInfrastructure(configuration);
        services.AddParser(configuration);

        var provider = services.BuildServiceProvider();

        // Act & Assert
        var service = provider.GetService<IMessageReprocessingService>();
        var contextBuilder = provider.GetService<IMessageContextBuilder>();

        Assert.NotNull(service);
        Assert.NotNull(contextBuilder);
    }
}
