using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.Monitoring;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Application.SignalIntelligence.Parser;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Parsers;
using TradingBot.Parser.Services;
using TradingBot.Telegram.Models;
using Xunit;
using AppRepos = TradingBot.Application.Repositories;

namespace TradingBot.UnitTests.SignalIntelligence;

public class AIMessageUnderstandingTests
{
    private readonly Mock<IAIProvider> _mockAiProvider = new();
    private readonly Mock<IPromptTemplateEngine> _mockPromptEngine = new();
    private readonly Mock<IBreakEvenManager> _mockBreakEvenManager = new();
    private readonly Mock<IPositionCloseManager> _mockPositionCloseManager = new();
    private readonly Mock<ISignalStorageQueue> _mockSignalQueue = new();
    private readonly Mock<IMessageRepository> _mockMessageRepo = new();
    private readonly Mock<AppRepos.ISignalRepository> _mockSignalRepo = new();
    private readonly Mock<AppRepos.IPositionRepository> _mockPositionRepo = new();
    private readonly Mock<AppRepos.IOrderRepository> _mockOrderRepo = new();
    private readonly Mock<ITelegramSourceRepository> _mockSourceRepo = new();

    private readonly AIIntentInterpreter _interpreter;
    private readonly TargetResolver _targetResolver;
    private readonly IntentSafetyValidator _safetyValidator;
    private readonly AIDecisionEngine _aiDecisionEngine;

    public AIMessageUnderstandingTests()
    {
        _mockPromptEngine.Setup(p => p.RenderPrompt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns("Rendered AI Prompt");

        _interpreter = new AIIntentInterpreter(_mockAiProvider.Object, _mockPromptEngine.Object, NullLogger<AIIntentInterpreter>.Instance);
        _targetResolver = new TargetResolver(NullLogger<TargetResolver>.Instance);
        _safetyValidator = new IntentSafetyValidator(Options.Create(new MessageIntelligenceOptions { MinimumConfidence = 0.80m }), NullLogger<IntentSafetyValidator>.Instance);
        _aiDecisionEngine = new AIDecisionEngine();
    }

    // A. Structured Entry Signal -> deterministic parser fast-path
    [Fact]
    public async Task TestA_StructuredEntrySignal_UsesDeterministicFastPath()
    {
        var text = "سیگنال\nجفت ارز: AUD/USD\nنوع معامله: خرید (BUY)\nنقطه ورود: 0.70920\nتارگت اول: 0.70110\nتارگت دوم: 0.71350\nتارگت سوم: 0.71500\nحد ضرر: 0.70730";
        var message = new TelegramMessage(100, 1, null, text, DateTime.UtcNow);

        var preprocessor = new MessagePreprocessor();
        var classifier = new TradingBot.Application.SignalIntelligence.Parser.MessageClassifier(preprocessor);
        var analysis = await classifier.ClassifyAsync(message);

        analysis.MessageType.Should().Be(MessageType.SIGNAL);
        analysis.Intent.Should().Be(TelegramMessageIntent.EntrySignal);

        var parsedResult = new ParsedMessageResult
        {
            Type = MessageType.SIGNAL,
            Symbol = "AUDUSD",
            Side = OrderSide.Buy,
            Entry = 0.70920m,
            StopLoss = 0.70730m,
            TakeProfits = new List<decimal> { 0.70110m, 0.71350m, 0.71500m },
            Confidence = 0.95m
        };

        var decision = _aiDecisionEngine.DetermineAIUsage(message, parsedResult);
        decision.ShouldUseAI.Should().BeFalse(); // Fast-path deterministic parsing
    }

    // B. "تارگت اول خورد" -> PositionManagement / PartialProfit
    [Fact]
    public async Task TestB_TargetHitMessage_ClassifiesAsPositionManagementPartialProfit()
    {
        var text = "تارگت اول خورد ✅";
        var message = new TelegramMessage(100, 2, null, text, DateTime.UtcNow);

        var preprocessor = new MessagePreprocessor();
        var classifier = new TradingBot.Application.SignalIntelligence.Parser.MessageClassifier(preprocessor);
        var analysis = await classifier.ClassifyAsync(message);

        analysis.MessageType.Should().Be(MessageType.TRADE_UPDATE);
        analysis.Intent.Should().Be(TelegramMessageIntent.PositionManagement);
        analysis.Action.Should().Be("TakePartialProfit");
    }

    // C. "تا قبل از تارگت اول..." (#48285) -> Informational/Conditional context, NOT TakePartialProfit
    [Fact]
    public async Task TestC_Message48285_ClassifiesAsInformationalContext_NotPartialProfit()
    {
        var text = "دوستانی که صبح میان و معاملات رو میبینن لطفا چارت رو نگاه کنید اگه معاملات فعال نشده بودن میتونن استفاده کنن و یک نکته مهم اینکه تا قبل از تارگت اول یا استاپ شدن معامله شما در نقطه ورود مجاز به وارد شده به سیگنال ها هستید";
        var message = new TelegramMessage(100, 48285, null, text, DateTime.UtcNow);

        var preprocessor = new MessagePreprocessor();
        var classifier = new TradingBot.Application.SignalIntelligence.Parser.MessageClassifier(preprocessor);
        var analysis = await classifier.ClassifyAsync(message);

        analysis.Intent.Should().Be(TelegramMessageIntent.Informational);
        analysis.Action.Should().Be("None");

        // Verify AI JSON interpretation returns Informational with NoAction
        var jsonResponse = @"{
            ""intent"": ""Informational"",
            ""action"": ""None"",
            ""is_trade_related"": false,
            ""is_executable_candidate"": false,
            ""symbol"": null,
            ""execution_mode"": ""Informational"",
            ""confidence"": 0.95,
            ""resolution_status"": ""NotTradingRelated"",
            ""reason"": ""Trading condition and rule explanation, not target hit or partial profit.""
        }";

        _mockAiProvider.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var context = new MessageIntelligenceContext { CurrentMessage = new CurrentMessageContext { MessageId = 48285, Text = text } };
        var intent = await _interpreter.InterpretAsync(context);

        intent.RequiresExecution.Should().BeFalse();
        intent.ExecutionMode.Should().Be(ExecutionMode.Informational);
        intent.ResolutionStatus.Should().Be(ResolutionStatus.NotTradingRelated);
    }

    // D. "دقیقا به 1.14720 واکنش داده..." (#48295) -> Analysis/Informational
    [Fact]
    public async Task TestD_Message48295_ClassifiesAsMarketAnalysis_PriceIsNotEntryPrice()
    {
        var text = "دقیقا به 1.14720 واکنش داده ولی خب چون فعال نشدیم معامله هنوز باز نشده برامون و منتظریم";
        var message = new TelegramMessage(100, 48295, null, text, DateTime.UtcNow);

        var preprocessor = new MessagePreprocessor();
        var classifier = new TradingBot.Application.SignalIntelligence.Parser.MessageClassifier(preprocessor);
        var analysis = await classifier.ClassifyAsync(message);

        analysis.MessageType.Should().Be(MessageType.ANALYSIS);
        analysis.Intent.Should().Be(TelegramMessageIntent.MarketAnalysis);

        var jsonResponse = @"{
            ""intent"": ""MarketAnalysis"",
            ""action"": ""None"",
            ""is_trade_related"": true,
            ""is_executable_candidate"": false,
            ""symbol"": ""EURUSD"",
            ""entry_price"": null,
            ""execution_mode"": ""Informational"",
            ""confidence"": 0.94,
            ""resolution_status"": ""NotTradingRelated"",
            ""reason"": ""Market commentary on price reaction. No order created.""
        }";

        _mockAiProvider.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var context = new MessageIntelligenceContext { CurrentMessage = new CurrentMessageContext { MessageId = 48295, Text = text } };
        var intent = await _interpreter.InterpretAsync(context);

        intent.RequiresExecution.Should().BeFalse();
        intent.EntryPrice.Should().BeNull(); // 1.14720 MUST NOT become EntryPrice
        intent.ExecutionMode.Should().Be(ExecutionMode.Informational);
    }

    // E. "ببندش" without context -> Ambiguous/InsufficientContext
    [Fact]
    public async Task TestE_CloseCommand_WithoutContext_IsInsufficientContext()
    {
        var text = "ببندش";
        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext { MessageId = 5, Text = text },
            ActivePositions = new List<PositionContextInfo>() // No active positions
        };

        var intent = new StructuredIntent
        {
            MessageType = IntelligenceMessageType.COMMAND,
            Intent = TradingIntent.CLOSE,
            RequiresExecution = true,
            IsTradeRelated = true,
            IsExecutableCandidate = true,
            Confidence = 0.90m
        };

        var resolved = await _targetResolver.ResolveTargetsAsync(intent, context);

        resolved.RequiresExecution.Should().BeFalse();
        resolved.ResolutionStatus.Should().Be(ResolutionStatus.InsufficientContext);
    }

    // F. "ببندش" replying to a unique active BTC position -> Resolved
    [Fact]
    public async Task TestF_CloseCommand_ReplyingToUniqueActivePosition_ResolvesSuccessfully()
    {
        var text = "ببندش";
        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext { MessageId = 6, Text = text, ReplyToMessageId = 100 },
            ReplyContext = new ReplyContextInfo
            {
                ReplyToMessageId = 100,
                Symbol = "BTCUSDT",
                OriginalMessageText = "BTCUSDT BUY Entry 60000"
            },
            ActivePositions = new List<PositionContextInfo>
            {
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "BTCUSDT", EntryPrice = 60000m }
            }
        };

        var intent = new StructuredIntent
        {
            MessageType = IntelligenceMessageType.COMMAND,
            Intent = TradingIntent.CLOSE,
            RequiresExecution = true,
            IsTradeRelated = true,
            IsExecutableCandidate = true,
            ExecutionMode = ExecutionMode.Immediate,
            Confidence = 0.95m
        };

        var resolved = await _targetResolver.ResolveTargetsAsync(intent, context);

        resolved.ResolutionStatus.Should().Be(ResolutionStatus.Resolved);
        resolved.Symbol.Should().Be("BTCUSDT");
        resolved.Targets.Should().ContainSingle("BTCUSDT");

        var (isSafe, _) = await _safetyValidator.ValidateSafetyAsync(resolved, context);
        isSafe.Should().BeTrue();
    }

    // G. "ببندش" with multiple possible positions -> Ambiguous
    [Fact]
    public async Task TestG_CloseCommand_WithMultiplePositionsAndNoReply_IsAmbiguous()
    {
        var text = "ببندش";
        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext { MessageId = 7, Text = text },
            ActivePositions = new List<PositionContextInfo>
            {
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "BTCUSDT" },
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "EURUSD" },
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "XAUUSD" }
            }
        };

        var intent = new StructuredIntent
        {
            MessageType = IntelligenceMessageType.COMMAND,
            Intent = TradingIntent.CLOSE,
            RequiresExecution = true,
            IsTradeRelated = true,
            IsExecutableCandidate = true,
            Confidence = 0.90m
        };

        var resolved = await _targetResolver.ResolveTargetsAsync(intent, context);

        resolved.RequiresExecution.Should().BeFalse();
        resolved.ResolutionStatus.Should().Be(ResolutionStatus.Ambiguous);

        var (isSafe, reason) = await _safetyValidator.ValidateSafetyAsync(resolved, context);
        isSafe.Should().BeFalse();
        reason.Should().Contain("does not require execution");
    }

    // H. "اگر برگشت روی ورود، استاپ رو سر به سر کن" -> ConditionalPositionManagement
    [Fact]
    public async Task TestH_ConditionalCommand_DoesNotExecuteImmediately()
    {
        var text = "اگر برگشت روی ورود، استاپ رو سر به سر کن";
        var jsonResponse = @"{
            ""intent"": ""ConditionalPositionManagement"",
            ""action"": ""MoveStopLossToEntry"",
            ""is_trade_related"": true,
            ""is_executable_candidate"": false,
            ""symbol"": ""EURUSD"",
            ""conditions"": [""PriceReturnsToEntry""],
            ""execution_mode"": ""Conditional"",
            ""confidence"": 0.95,
            ""resolution_status"": ""Resolved"",
            ""reason"": ""Conditional breakeven instruction""
        }";

        _mockAiProvider.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext { MessageId = 8, Text = text },
            ActivePositions = new List<PositionContextInfo>
            {
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "EURUSD", EntryPrice = 1.0800m }
            }
        };

        var intent = await _interpreter.InterpretAsync(context);
        intent = await _targetResolver.ResolveTargetsAsync(intent, context);

        intent.ExecutionMode.Should().Be(ExecutionMode.Conditional);
        intent.RequiresExecution.Should().BeFalse(); // MUST NOT execute immediately

        var (isSafe, reason) = await _safetyValidator.ValidateSafetyAsync(intent, context);
        isSafe.Should().BeFalse();
        reason.Should().Contain("does not require execution");
    }

    // I. "ریسک فری شد" with matching active position -> MoveStopLossToEntry
    [Fact]
    public async Task TestI_RiskFreeDone_WithMatchingPosition_ResolvesMoveStopToEntry()
    {
        var text = "یورو ریسک فری شد";
        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext { MessageId = 9, Text = text },
            ActivePositions = new List<PositionContextInfo>
            {
                new PositionContextInfo { PositionId = Guid.NewGuid(), Symbol = "EURUSD", EntryPrice = 1.0800m }
            }
        };

        var intent = new StructuredIntent
        {
            MessageType = IntelligenceMessageType.COMMAND,
            Intent = TradingIntent.RISK_FREE,
            Scope = IntentScope.SYMBOL,
            Targets = new List<string> { "EURUSD" },
            Symbol = "EURUSD",
            RequiresExecution = true,
            IsTradeRelated = true,
            IsExecutableCandidate = true,
            ExecutionMode = ExecutionMode.Immediate,
            Confidence = 0.95m
        };

        var resolved = await _targetResolver.ResolveTargetsAsync(intent, context);
        resolved.ResolutionStatus.Should().Be(ResolutionStatus.Resolved);

        var (isSafe, _) = await _safetyValidator.ValidateSafetyAsync(resolved, context);
        isSafe.Should().BeTrue();
    }

    // J. "ریسک فری شد" without matching position -> unresolved / no execution
    [Fact]
    public async Task TestJ_RiskFreeDone_WithoutMatchingPosition_FailsSafetyCheck()
    {
        var text = "یورو ریسک فری شد";
        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext { MessageId = 10, Text = text },
            ActivePositions = new List<PositionContextInfo>() // No EURUSD position active
        };

        var intent = new StructuredIntent
        {
            MessageType = IntelligenceMessageType.COMMAND,
            Intent = TradingIntent.RISK_FREE,
            Scope = IntentScope.SYMBOL,
            Targets = new List<string> { "EURUSD" },
            Symbol = "EURUSD",
            RequiresExecution = true,
            IsTradeRelated = true,
            IsExecutableCandidate = true,
            ExecutionMode = ExecutionMode.Immediate,
            ResolutionStatus = ResolutionStatus.Resolved,
            Confidence = 0.95m
        };

        var (isSafe, reason) = await _safetyValidator.ValidateSafetyAsync(intent, context);

        isSafe.Should().BeFalse();
        reason.Should().Contain("No open active position found");
    }

    // K. Structured signal followed by "وارد نشید" -> context-aware instruction (no second EntrySignal)
    [Fact]
    public async Task TestK_DoNotEnterInstruction_FollowedBySignal_DoesNotCreateSecondEntrySignal()
    {
        var text = "فعلاً وارد نشید";
        var jsonResponse = @"{
            ""intent"": ""Informational"",
            ""action"": ""Wait"",
            ""is_trade_related"": true,
            ""is_executable_candidate"": false,
            ""symbol"": ""EURUSD"",
            ""execution_mode"": ""NoAction"",
            ""confidence"": 0.96,
            ""resolution_status"": ""NotTradingRelated"",
            ""reason"": ""Instruction not to enter signal immediately.""
        }";

        _mockAiProvider.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext { MessageId = 11, Text = text, ReplyToMessageId = 10 }
        };

        var intent = await _interpreter.InterpretAsync(context);

        intent.MessageType.Should().NotBe(IntelligenceMessageType.NEW_SIGNAL);
        intent.RequiresExecution.Should().BeFalse();
        intent.ExecutionMode.Should().Be(ExecutionMode.NoAction);
    }

    // L. Message referring to previous signal through ReplyToMessageId -> AI inspects referenced signal
    [Fact]
    public async Task TestL_MessageWithReplyToMessageId_InspectsReferencedSignalContext()
    {
        var text = "همین رو نگه دارید";
        var jsonResponse = @"{
            ""intent"": ""PositionManagement"",
            ""action"": ""None"",
            ""is_trade_related"": true,
            ""is_executable_candidate"": false,
            ""symbol"": ""GBPUSD"",
            ""execution_mode"": ""NoAction"",
            ""confidence"": 0.93,
            ""resolution_status"": ""Resolved"",
            ""reason"": ""Hold existing position referenced in reply.""
        }";

        _mockAiProvider.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext { MessageId = 12, Text = text, ReplyToMessageId = 101 },
            ReplyContext = new ReplyContextInfo
            {
                ReplyToMessageId = 101,
                Symbol = "GBPUSD",
                OriginalMessageText = "GBPUSD BUY Entry 1.2600"
            }
        };

        var intent = await _interpreter.InterpretAsync(context);
        var resolved = await _targetResolver.ResolveTargetsAsync(intent, context);

        resolved.Symbol.Should().Be("GBPUSD");
        resolved.ResolutionStatus.Should().Be(ResolutionStatus.NotTradingRelated);
        resolved.RequiresExecution.Should().BeFalse();
    }
}
