using TradingBot.Application.Trading.Execution.Models;

namespace TradingBot.Application.Trading.Execution.Contracts;

public interface IOrderBuilder
{
    OrderRequest Build(TradeExecutionRequest request);
}
