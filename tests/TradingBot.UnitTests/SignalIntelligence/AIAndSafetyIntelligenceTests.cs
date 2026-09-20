using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Services;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class AIAndSafetyIntelligenceTests
{
    [Fact]
    public async Task AIIntentInterpreter_ParsesValidJsonResponse_Successfully()
    {
        // Arrange
        var mockAiProvider = new Mock<IAIProvider>();
        var mockPromptEngine = new Mock<IPromptTemplateEngine>();

        mockPromptEngine.Setup(p => p.RenderPrompt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns("Rendered Prompt");

        string jsonResponse = @"{
            ""messageType"": ""COMMAND"",
            ""intent"": ""RISK_FREE"",
            ""scope"": ""SYMBOL"",
            ""targets"": [""EURUSD""],
            ""confidence"": 0.95,
            ""requiresExecution"": true,
            ""reason"": ""AI classified risk free command""
        }";

        mockAiProvider.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var interpreter = new AIIntentInterpreter(
            mockAiProvider.Object,
            mockPromptEngine.Object,
            NullLogger<AIIntentInterpreter>.Instance
        );

        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext
            {
                MessageId = 1,
                ChannelId = 100,
                Text = "حالا این معامله یورو رو سر به سر کنید",
                Date = DateTime.UtcNow
            }
        };

        // Act
        var result = await interpreter.InterpretAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(IntelligenceMessageType.COMMAND, result.MessageType);
        Assert.Equal(TradingIntent.RISK_FREE, result.Intent);
        Assert.Equal(IntentScope.SYMBOL, result.Scope);
        Assert.Single(result.Targets);
        Assert.Equal("EURUSD", result.Targets.First());
        Assert.Equal(0.95m, result.Confidence);
        Assert.True(result.RequiresExecution);
        Assert.Equal("AI", result.DetectionMethod);
    }

    [Fact]
    public async Task AIIntentInterpreter_HandlesExceptionAndReturnsUnknown_Safely()
    {
        // Arrange
        var mockAiProvider = new Mock<IAIProvider>();
        var mockPromptEngine = new Mock<IPromptTemplateEngine>();

        mockPromptEngine.Setup(p => p.RenderPrompt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns("Rendered Prompt");

        mockAiProvider.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AI Service Unavailable"));

        var interpreter = new AIIntentInterpreter(
            mockAiProvider.Object,
            mockPromptEngine.Object,
            NullLogger<AIIntentInterpreter>.Instance
        );

        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext
            {
                MessageId = 1,
                ChannelId = 100,
                Text = "پیام نامشخص",
                Date = DateTime.UtcNow
            }
        };

        // Act
        var result = await interpreter.InterpretAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(IntelligenceMessageType.UNKNOWN, result.MessageType);
        Assert.Equal(TradingIntent.UNKNOWN, result.Intent);
        Assert.False(result.RequiresExecution);
        Assert.Equal(0.0m, result.Confidence);
    }

    [Fact]
    public async Task TargetResolver_ResolvesExplicitAndSinglePositionTargets_Correctly()
    {
        // Arrange
        var resolver = new TargetResolver(NullLogger<TargetResolver>.Instance);

        var intentWithoutTargets = new StructuredIntent
        {
            MessageType = IntelligenceMessageType.COMMAND,
            Intent = TradingIntent.RISK_FREE,
            RequiresExecution = true,
            Confidence = 0.90m
        };

        var contextWithSinglePos = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext { MessageId = 5, Text = "ریسک فری کنید" },
            ActivePositions = new List<PositionContextInfo>
            {
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "GBPUSD", EntryPrice = 1.2500m }
            }
        };

        // Act
        var resolved = await resolver.ResolveTargetsAsync(intentWithoutTargets, contextWithSinglePos);

        // Assert
        Assert.NotNull(resolved);
        Assert.Single(resolved.Targets);
        Assert.Equal("GBPUSD", resolved.Targets.First());
        Assert.Equal(IntentScope.SYMBOL, resolved.Scope);
    }

    [Fact]
    public async Task TargetResolver_MarksAmbiguous_WhenMultipleTargetsAndNoExplicitSymbol()
    {
        // Arrange
        var resolver = new TargetResolver(NullLogger<TargetResolver>.Instance);

        var intentWithoutTargets = new StructuredIntent
        {
            MessageType = IntelligenceMessageType.COMMAND,
            Intent = TradingIntent.RISK_FREE,
            RequiresExecution = true,
            Confidence = 0.90m
        };

        var contextWithMultiplePos = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext { MessageId = 5, Text = "ریسک فری کنید" },
            ActivePositions = new List<PositionContextInfo>
            {
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "EURUSD", EntryPrice = 1.0800m },
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "GBPUSD", EntryPrice = 1.2500m }
            }
        };

        // Act
        var resolved = await resolver.ResolveTargetsAsync(intentWithoutTargets, contextWithMultiplePos);

        // Assert
        Assert.NotNull(resolved);
        Assert.False(resolved.RequiresExecution); // Execution disabled due to ambiguity!
    }

    [Fact]
    public async Task IntentSafetyValidator_BlocksExecution_WhenConfidenceBelowThreshold()
    {
        // Arrange
        var options = Options.Create(new MessageIntelligenceOptions { MinimumConfidence = 0.85m });
        var validator = new IntentSafetyValidator(options, NullLogger<IntentSafetyValidator>.Instance);

        var lowConfidenceIntent = new StructuredIntent
        {
            MessageType = IntelligenceMessageType.COMMAND,
            Intent = TradingIntent.RISK_FREE,
            RequiresExecution = true,
            Confidence = 0.70m, // Below 0.85
            Targets = new List<string> { "EURUSD" }
        };

        var context = new MessageIntelligenceContext();

        // Act
        var (isSafe, reason) = await validator.ValidateSafetyAsync(lowConfidenceIntent, context);

        // Assert
        Assert.False(isSafe);
        Assert.Contains("Confidence", reason);
    }
}
