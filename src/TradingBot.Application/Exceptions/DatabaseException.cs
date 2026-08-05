using System;

namespace TradingBot.Application.Exceptions;

public class DatabaseException : ApplicationException
{
    public DatabaseException() : base() { }

    public DatabaseException(string message) : base(message) { }

    public DatabaseException(string message, Exception innerException) : base(message, innerException) { }
}
