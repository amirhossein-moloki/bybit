using System.Threading.Tasks;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Interfaces;

public interface ISignalExtractor
{
    Task ExtractAsync(
        ParserContext context,
        ParsedSignal signal
    );
}
