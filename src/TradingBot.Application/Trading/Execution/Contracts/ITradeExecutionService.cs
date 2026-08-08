using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Trading.Execution.Models;

namespace TradingBot.Application.Trading.Execution.Contracts;

public interface ITradeExecutionService
{
    Task<ExecutionResult> ExecuteAsync(TradeExecutionRequest request, CancellationToken cancellationToken = default);
}
