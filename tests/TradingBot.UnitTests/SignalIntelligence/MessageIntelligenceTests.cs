using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;
using TradingBot.Parser.Services;
using TradingBot.Telegram.Models;
using AppRepos = TradingBot.Application.Repositories;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class MessageIntelligenceTests
{
    [Fact]
    public void TelegramMessage_MetadataProperties_AreAssignedCorrectly()
    {
        // Arrange & Act
        var msg = new TelegramMessage(
            channelId: -100123456789,
            messageId: 42,
            senderId: 999,
            content: "ریسک فری کنید",
            receivedAt: DateTime.UtcNow,
            replyToMessageId: 10,
            mediaInfo: "MessageMediaPhoto",
            editInfo: "EditedAt:2026-08-21T10:00:00Z"
        );

        // Assert
        Assert.Equal(-100123456789, msg.ChannelId);
        Assert.Equal(42, msg.MessageId);
        Assert.Equal(999, msg.SenderId);
        Assert.Equal("ریسک فری کنید", msg.Content);
        Assert.Equal(10, msg.ReplyToMessageId);
        Assert.Equal("MessageMediaPhoto", msg.MediaInfo);
        Assert.Equal("EditedAt:2026-08-21T10:00:00Z", msg.EditInfo);
    }

    [Fact]
    public async Task MessageContextBuilder_ResolvesReplyAndConversationContext_Successfully()
    {
        // Arrange
        var mockMsgRepo = new Mock<IMessageRepository>();
        var mockSigRepo = new Mock<AppRepos.ISignalRepository>();
        var mockPosRepo = new Mock<AppRepos.IPositionRepository>();
        var mockOrderRepo = new Mock<AppRepos.IOrderRepository>();
        var mockSourceRepo = new Mock<ITelegramSourceRepository>();

        long channelId = -100100200300;
        long replyMsgId = 10;

        var origTelegramMsg = new TelegramMessage(channelId, replyMsgId, 1, "BUY EURUSD Entry 1.0850 SL 1.0800", DateTime.UtcNow);
        mockMsgRepo.Setup(r => r.GetByChannelMessageIdAsync(channelId, replyMsgId, default))
            .ReturnsAsync(origTelegramMsg);

        var origSignal = new Signal(channelId, replyMsgId, "BUY EURUSD Entry 1.0850 SL 1.0800", "EURUSD", OrderSide.Buy, DateTime.UtcNow);
        mockSigRepo.Setup(r => r.GetPendingSignalsAsync(default))
            .ReturnsAsync(new List<Signal> { origSignal });

        var recentMsgs = new List<TelegramMessage>
        {
            new TelegramMessage(channelId, 11, 1, "نزدیکه حواسا جمع", DateTime.UtcNow),
            new TelegramMessage(channelId, 12, 1, "ریسک فری کنید", DateTime.UtcNow)
        };
        mockMsgRepo.Setup(r => r.GetRecentMessagesForChannelAsync(channelId, 5, default))
            .ReturnsAsync(recentMsgs);

        var builder = new MessageContextBuilder(
            NullLogger<MessageContextBuilder>.Instance,
            mockMsgRepo.Object,
            mockSigRepo.Object,
            mockPosRepo.Object,
            mockOrderRepo.Object,
            mockSourceRepo.Object
        );

        var dto = new TelegramMessageDto
        {
            ChannelId = channelId,
            MessageId = 12,
            SenderId = 1,
            Text = "ریسک فری کنید",
            Date = DateTime.UtcNow,
            ReplyToMessageId = replyMsgId
        };

        // Act
        var context = await builder.BuildContextAsync(dto);

        // Assert
        Assert.NotNull(context);
        Assert.Equal(12, context.CurrentMessage.MessageId);
        Assert.Equal(replyMsgId, context.CurrentMessage.ReplyToMessageId);
        Assert.NotNull(context.ReplyContext);
        Assert.Equal("BUY EURUSD Entry 1.0850 SL 1.0800", context.ReplyContext.OriginalMessageText);
        Assert.Equal("EURUSD", context.ReplyContext.Symbol);
        Assert.Single(context.ConversationContext);
        Assert.Equal(11, context.ConversationContext.First().MessageId);
    }

    [Theory]
    [InlineData("ریسک فری کنید", IntelligenceMessageType.COMMAND, TradingIntent.RISK_FREE, true)]
    [InlineData("همه معاملات رو ریسک فری کنید", IntelligenceMessageType.COMMAND, TradingIntent.RISK_FREE_ALL, true)]
    [InlineData("دوستان اوردر ها رو کنسل کنید", IntelligenceMessageType.COMMAND, TradingIntent.CANCEL_PENDING_ORDERS, true)]
    [InlineData("امشب اخبار مهمی داریم همه اوردر هایی ک فعال نشدن رو کنسل کنید", IntelligenceMessageType.COMMAND, TradingIntent.CANCEL_PENDING_ORDERS, true)]
    [InlineData("معامله را ببندید", IntelligenceMessageType.COMMAND, TradingIntent.CLOSE, true)]
    [InlineData("اوردر ها رو برگردونید", IntelligenceMessageType.COMMAND, TradingIntent.RESTORE_ORDER, true)]
    [InlineData("ریسک فری شد", IntelligenceMessageType.STATUS, TradingIntent.STATUS_REPORT, false)]
    [InlineData("یورو ریسک فری شده", IntelligenceMessageType.STATUS, TradingIntent.STATUS_REPORT, false)]
    [InlineData("فعاله", IntelligenceMessageType.STATUS, TradingIntent.STATUS_REPORT, false)]
    [InlineData("تارگت اول✅", IntelligenceMessageType.STATUS, TradingIntent.STATUS_REPORT, false)]
    [InlineData("پوند استاپ شد..", IntelligenceMessageType.STATUS, TradingIntent.STATUS_REPORT, false)]
    [InlineData("چهارتا اوردر داریم منتظریم که ببینیم چی میشه", IntelligenceMessageType.COMMENTARY, TradingIntent.NO_ACTION, false)]
    [InlineData("پوند معتبره", IntelligenceMessageType.COMMENTARY, TradingIntent.NO_ACTION, false)]
    [InlineData("وضعیت معاملاتتون رو اعلام کنید", IntelligenceMessageType.QUESTION, TradingIntent.NO_ACTION, false)]
    public async Task DeterministicRuleEngine_ClassifiesSampleMessages_Correctly(
        string messageText,
        IntelligenceMessageType expectedType,
        TradingIntent expectedIntent,
        bool expectedExecution)
    {
        // Arrange
        var engine = new DeterministicRuleEngine(NullLogger<DeterministicRuleEngine>.Instance);
        var context = new MessageIntelligenceContext
        {
            CurrentMessage = new CurrentMessageContext
            {
                MessageId = 100,
                ChannelId = -1001,
                Text = messageText,
                Date = DateTime.UtcNow
            }
        };

        // Act
        var result = await engine.EvaluateAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedType, result.MessageType);
        Assert.Equal(expectedIntent, result.Intent);
        Assert.Equal(expectedExecution, result.RequiresExecution);
    }
}
