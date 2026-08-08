using System;

namespace TradingBot.Application.Trading.Execution.Exceptions;

public class OrderConstructionException : Exception
{
    public OrderConstructionException(string message) : base(message)
    {
    }

    public OrderConstructionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
