using System;

namespace TradingBot.Application.Exceptions;

public class CircuitOpenedException : ApplicationException
{
    public CircuitOpenedException() : base() { }

    public CircuitOpenedException(string message) : base(message) { }

    public CircuitOpenedException(string message, Exception innerException) : base(message, innerException) { }
}
