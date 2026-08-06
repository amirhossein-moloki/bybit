using System;

namespace TradingBot.Application.RiskManagement.Exceptions;

public class RiskManagementException : TradingBot.Application.Exceptions.ApplicationException
{
    public RiskManagementException() : base() { }

    public RiskManagementException(string message) : base(message) { }

    public RiskManagementException(string message, Exception innerException) : base(message, innerException) { }
}
