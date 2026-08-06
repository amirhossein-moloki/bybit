using System.Threading.Tasks;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Interfaces;

public interface ISignalParser
{
    Task<ParserResult> ParseAsync(
        ParserContext context
    );
}
