using Xunit;
using FluentAssertions;
using TradingBot.Telegram.Client;

namespace TradingBot.UnitTests.Telegram;

public class TelegramChatIdNormalizationTests
{
    [Theory]
    [InlineData(1492324861, 1492324861)]
    [InlineData(-1001492324861, 1492324861)]
    [InlineData(1001492324861, 1492324861)]
    [InlineData(-1492324861, 1492324861)]
    public void NormalizeChatId_ShouldReturnNormalizedRawNumber(long input, long expected)
    {
        var result = TelegramClientService.NormalizeChatId(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1492324861, -1001492324861, true)]
    [InlineData(-1001492324861, 1492324861, true)]
    [InlineData(1492324861, 1492324861, true)]
    [InlineData(-1001492324861, -1001492324861, true)]
    [InlineData(1492324861, 9876543210, false)]
    public void AreChatIdsEqual_ShouldCorrectlyCompareDifferentRepresentations(long id1, long id2, bool expected)
    {
        var result = TelegramClientService.AreChatIdsEqual(id1, id2);
        result.Should().Be(expected);
    }
}
