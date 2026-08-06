using System;

namespace TradingBot.Telegram.Exceptions;

public class TelegramAuthenticationException : Exception
{
    public TelegramAuthenticationException() { }
    public TelegramAuthenticationException(string message) : base(message) { }
    public TelegramAuthenticationException(string message, Exception innerException) : base(message, innerException) { }
}

public class TelegramConnectionException : Exception
{
    public TelegramConnectionException() { }
    public TelegramConnectionException(string message) : base(message) { }
    public TelegramConnectionException(string message, Exception innerException) : base(message, innerException) { }
}

public class TelegramSessionException : Exception
{
    public TelegramSessionException() { }
    public TelegramSessionException(string message) : base(message) { }
    public TelegramSessionException(string message, Exception innerException) : base(message, innerException) { }
}

public class InvalidTelegramConfigurationException : TelegramConnectionException
{
    public InvalidTelegramConfigurationException() { }
    public InvalidTelegramConfigurationException(string message) : base(message) { }
    public InvalidTelegramConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}
