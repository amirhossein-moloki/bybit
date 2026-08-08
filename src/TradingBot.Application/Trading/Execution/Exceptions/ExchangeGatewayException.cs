using System;

namespace TradingBot.Application.Trading.Execution.Exceptions;

public class ExchangeGatewayException : Exception
{
    public ExchangeGatewayException(string message) : base(message)
    {
    }

    public ExchangeGatewayException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
