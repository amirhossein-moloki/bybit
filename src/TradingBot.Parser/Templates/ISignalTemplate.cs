using System.Collections.Generic;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Templates;

public interface ISignalTemplate
{
    bool CanHandle(ParserContext context);
    IReadOnlyList<TemplateRule> GetRules();
}
