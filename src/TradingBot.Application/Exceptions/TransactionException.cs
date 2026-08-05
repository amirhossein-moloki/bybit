using System;

namespace TradingBot.Application.Exceptions;

public class TransactionException : ApplicationException
{
    public TransactionException() : base() { }

    public TransactionException(string message) : base(message) { }

    public TransactionException(string message, Exception innerException) : base(message, innerException) { }
}
