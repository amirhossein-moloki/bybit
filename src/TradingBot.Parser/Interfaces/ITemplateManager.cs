using System.Threading.Tasks;
using TradingBot.Parser.Models;
using TradingBot.Parser.Templates;

namespace TradingBot.Parser.Interfaces;

public interface ITemplateManager
{
    Task<ISignalTemplate?> FindTemplateAsync(ParserContext context);
}
