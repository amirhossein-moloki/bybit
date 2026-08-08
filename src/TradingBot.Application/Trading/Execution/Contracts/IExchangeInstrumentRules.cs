using TradingBot.Application.Trading.Execution.Models;

namespace TradingBot.Application.Trading.Execution.Contracts;

public interface IExchangeInstrumentRules
{
    InstrumentRules? GetInstrumentRules(string symbol);
}
