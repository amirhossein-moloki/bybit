using System;

namespace TradingBot.Exchange.Bybit.Exceptions;

public class ExchangeException : Exception
{
    public ExchangeException() : base() { }

    public ExchangeException(string message) : base(message) { }

    public ExchangeException(string message, Exception innerException) : base(message, innerException) { }
}
