using System;

namespace TradingBot.Application.Trading.Execution.Exceptions;

public class ExecutionRejectedException : Exception
{
    public ExecutionRejectedException(string message) : base(message)
    {
    }

    public ExecutionRejectedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
