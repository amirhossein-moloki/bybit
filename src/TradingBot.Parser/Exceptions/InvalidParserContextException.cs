using System;

namespace TradingBot.Parser.Exceptions;

public class InvalidParserContextException : ParserException
{
    public InvalidParserContextException(string message) : base(message)
    {
    }

    public InvalidParserContextException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
