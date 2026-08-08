using TradingBot.Application.Trading.Execution.Models;

namespace TradingBot.Application.Trading.Execution.Contracts;

public interface IOrderValidator
{
    void Validate(TradeExecutionRequest request);
}
