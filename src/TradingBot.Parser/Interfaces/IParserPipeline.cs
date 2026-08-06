using System.Threading.Tasks;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Interfaces;

public interface IParserPipeline
{
    Task<ParsedSignal> ExecuteAsync(
        ParserContext context
    );
}
