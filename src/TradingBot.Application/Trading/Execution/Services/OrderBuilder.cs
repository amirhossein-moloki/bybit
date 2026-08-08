using System;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;

namespace TradingBot.Application.Trading.Execution.Services;

public class OrderBuilder : IOrderBuilder
{
    public OrderRequest Build(TradeExecutionRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return new OrderRequest
        {
            Symbol = SymbolNormalizer.Normalize(request.Symbol),
            Side = request.Side,
            Type = request.OrderType,
            Quantity = request.Quantity,
            Price = request.Price,
            SignalId = request.SignalId,
            RiskEvaluationId = request.RiskEvaluationId,
            ClientOrderId = $"BOT-{Guid.NewGuid():N}"
        };
    }
}
