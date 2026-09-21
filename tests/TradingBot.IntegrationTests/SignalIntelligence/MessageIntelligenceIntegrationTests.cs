using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Services;
using TradingBot.Application.Models;
using Xunit;

namespace TradingBot.IntegrationTests.SignalIntelligence;

public class MessageIntelligenceIntegrationTests
{
    [Fact]
    public async Task HappyPath_CommandMessage_TriggersBreakEvenExecution()
    {
        // Arrange
        var mockContextBuilder = new Mock<IMessageContextBuilder>();
        var mockAiInterpreter = new Mock<IAIIntentInterpreter>();
        var mockBreakEvenManager = new Mock<IBreakEvenManager>();
        var mockCloseManager = new Mock<IPositionCloseManager>();

        long channelId = -100123;
        long msgId = 50;

        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext
            {
                MessageId = msgId,
                ChannelId = channelId,
                Text = "همه معاملات رو ریسک فری کنید",
                Date = DateTime.UtcNow
            },
            ActivePositions = new List<PositionContextInfo>
            {
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "EURUSD", EntryPrice = 1.0850m }
            }
        };

        mockContextBuilder.Setup(b => b.BuildContextAsync(It.IsAny<TelegramMessageDto>(), default))
            .ReturnsAsync(context);

        var ruleEngine = new DeterministicRuleEngine(NullLogger<DeterministicRuleEngine>.Instance);
        var targetResolver = new TargetResolver(NullLogger<TargetResolver>.Instance);
        var safetyValidator = new IntentSafetyValidator(Options.Create(new MessageIntelligenceOptions { MinimumConfidence = 0.85m }), NullLogger<IntentSafetyValidator>.Instance);

        var router = new IntentExecutionRouter(
            mockContextBuilder.Object,
            ruleEngine,
            mockAiInterpreter.Object,
            targetResolver,
            safetyValidator,
            Options.Create(new MessageIntelligenceOptions { ShadowMode = false }),
            NullLogger<IntentExecutionRouter>.Instance,
            breakEvenManager: mockBreakEvenManager.Object,
            positionCloseManager: mockCloseManager.Object
        );

        var dto = new TelegramMessageDto
        {
            ChannelId = channelId,
            MessageId = (int)msgId,
            Text = "همه معاملات رو ریسک فری کنید",
            Date = DateTime.UtcNow
        };

        // Act
        await router.ProcessMessageIntelligenceAsync(dto);

        // Assert
        mockBreakEvenManager.Verify(b => b.ExecuteBreakEvenCheckAsync(
            It.IsAny<Guid>(),
            It.IsAny<decimal>(),
            It.IsAny<BreakEvenSettings>(),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task StatusReportMessage_StatusReport_GeneratesZeroExchangeMutations()
    {
        // Arrange
        var mockContextBuilder = new Mock<IMessageContextBuilder>();
        var mockAiInterpreter = new Mock<IAIIntentInterpreter>();
        var mockBreakEvenManager = new Mock<IBreakEvenManager>();

        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext
            {
                MessageId = 51,
                ChannelId = -100123,
                Text = "یورو ریسک فری شده",
                Date = DateTime.UtcNow
            },
            ActivePositions = new List<PositionContextInfo>
            {
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "EURUSD", EntryPrice = 1.0850m }
            }
        };

        mockContextBuilder.Setup(b => b.BuildContextAsync(It.IsAny<TelegramMessageDto>(), default))
            .ReturnsAsync(context);

        var ruleEngine = new DeterministicRuleEngine(NullLogger<DeterministicRuleEngine>.Instance);
        var targetResolver = new TargetResolver(NullLogger<TargetResolver>.Instance);
        var safetyValidator = new IntentSafetyValidator(Options.Create(new MessageIntelligenceOptions()), NullLogger<IntentSafetyValidator>.Instance);

        var router = new IntentExecutionRouter(
            mockContextBuilder.Object,
            ruleEngine,
            mockAiInterpreter.Object,
            targetResolver,
            safetyValidator,
            Options.Create(new MessageIntelligenceOptions { ShadowMode = false }),
            NullLogger<IntentExecutionRouter>.Instance,
            breakEvenManager: mockBreakEvenManager.Object
        );

        var dto = new TelegramMessageDto
        {
            ChannelId = -100123,
            MessageId = 51,
            Text = "یورو ریسک فری شده",
            Date = DateTime.UtcNow
        };

        // Act
        await router.ProcessMessageIntelligenceAsync(dto);

        // Assert: Zero Exchange Calls executed!
        mockBreakEvenManager.Verify(b => b.ExecuteBreakEvenCheckAsync(
            It.IsAny<Guid>(),
            It.IsAny<decimal>(),
            It.IsAny<BreakEvenSettings>(),
            default
        ), Times.Never);
    }

    [Fact]
    public async Task ShadowMode_Enabled_SimulatesExecutionWithZeroExchangeCalls()
    {
        // Arrange
        var mockContextBuilder = new Mock<IMessageContextBuilder>();
        var mockAiInterpreter = new Mock<IAIIntentInterpreter>();
        var mockBreakEvenManager = new Mock<IBreakEvenManager>();

        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext
            {
                MessageId = 52,
                ChannelId = -100123,
                Text = "ریسک فری کنید",
                Date = DateTime.UtcNow
            },
            ActivePositions = new List<PositionContextInfo>
            {
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "EURUSD", EntryPrice = 1.0850m }
            }
        };

        mockContextBuilder.Setup(b => b.BuildContextAsync(It.IsAny<TelegramMessageDto>(), default))
            .ReturnsAsync(context);

        var ruleEngine = new DeterministicRuleEngine(NullLogger<DeterministicRuleEngine>.Instance);
        var targetResolver = new TargetResolver(NullLogger<TargetResolver>.Instance);
        var safetyValidator = new IntentSafetyValidator(Options.Create(new MessageIntelligenceOptions()), NullLogger<IntentSafetyValidator>.Instance);

        var router = new IntentExecutionRouter(
            mockContextBuilder.Object,
            ruleEngine,
            mockAiInterpreter.Object,
            targetResolver,
            safetyValidator,
            Options.Create(new MessageIntelligenceOptions { ShadowMode = true }), // SHADOW MODE ACTIVE
            NullLogger<IntentExecutionRouter>.Instance,
            breakEvenManager: mockBreakEvenManager.Object
        );

        var dto = new TelegramMessageDto
        {
            ChannelId = -100123,
            MessageId = 52,
            Text = "ریسک فری کنید",
            Date = DateTime.UtcNow
        };

        // Act
        await router.ProcessMessageIntelligenceAsync(dto);

        // Assert: Zero Exchange Mutations in Shadow Mode!
        mockBreakEvenManager.Verify(b => b.ExecuteBreakEvenCheckAsync(
            It.IsAny<Guid>(),
            It.IsAny<decimal>(),
            It.IsAny<BreakEvenSettings>(),
            default
        ), Times.Never);
    }
}
