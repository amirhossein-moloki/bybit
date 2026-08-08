using System;

namespace TradingBot.Application.Trading.Execution.Exceptions;

public class ExecutionValidationException : Exception
{
    public ExecutionValidationException(string message) : base(message)
    {
    }

    public ExecutionValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
