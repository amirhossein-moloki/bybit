using System;

namespace TradingBot.Parser.Exceptions;

public class ParserExecutionException : ParserException
{
    public ParserExecutionException(string message) : base(message)
    {
    }

    public ParserExecutionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
