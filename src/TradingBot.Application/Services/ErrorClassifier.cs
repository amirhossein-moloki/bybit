using System;
using System.Net.Http;
using TradingBot.Application.Enums;
using TradingBot.Application.Exceptions;
using TradingBot.Application.Interfaces;

namespace TradingBot.Application.Services;

public class ErrorClassifier : IErrorClassifier
{
    public ErrorRetryability Classify(Exception exception)
    {
        if (exception == null) return ErrorRetryability.Unknown;

        // Unwrap AggregateException if present
        if (exception is AggregateException aggEx && aggEx.InnerException != null)
        {
            return Classify(aggEx.InnerException);
        }

        // CircuitOpenedException is non-retryable (fail fast)
        if (exception is CircuitOpenedException)
        {
            return ErrorRetryability.NonRetryable;
        }

        // RateLimitException is retryable
        if (exception is RateLimitException)
        {
            return ErrorRetryability.Retryable;
        }

        // OperationCanceledException (either timeout or user cancellation)
        if (exception is OperationCanceledException)
        {
            return ErrorRetryability.NonRetryable;
        }

        // System timeout
        if (exception is TimeoutException)
        {
            return ErrorRetryability.Retryable;
        }

        // Polly's timeout exception (it might be a TimeoutRejectedException from Polly)
        if (exception.GetType().FullName == "Polly.Timeout.TimeoutRejectedException")
        {
            return ErrorRetryability.Retryable;
        }

        // HTTP Exceptions
        if (exception is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                if (code == 408 || code == 429 || code >= 500)
                {
                    return ErrorRetryability.Retryable;
                }
                if (code == 400 || code == 401 || code == 403 || code == 404)
                {
                    return ErrorRetryability.NonRetryable;
                }
            }

            // Fallback message inspection for HTTP statuses
            var msg = httpEx.Message;
            if (msg.Contains("408") || msg.Contains("429") || msg.Contains("Too Many Requests") ||
                msg.Contains("500") || msg.Contains("502") || msg.Contains("503") || msg.Contains("504"))
            {
                return ErrorRetryability.Retryable;
            }

            if (msg.Contains("400") || msg.Contains("401") || msg.Contains("403") || msg.Contains("404"))
            {
                return ErrorRetryability.NonRetryable;
            }

            // Connection reset, socket exception, DNS failure, etc., are transient
            return ErrorRetryability.Retryable;
        }

        // Telegram Exceptions (inspected by type name strings to respect Clean Architecture decoupling)
        var exceptionTypeName = exception.GetType().Name;
        if (exceptionTypeName == "TelegramAuthenticationException" ||
            exceptionTypeName == "InvalidTelegramConfigurationException" ||
            exceptionTypeName == "TelegramSessionException")
        {
            return ErrorRetryability.NonRetryable;
        }

        if (exceptionTypeName == "TelegramConnectionException")
        {
            return ErrorRetryability.Retryable;
        }

        // Message-based or typed exchange exception mappings
        var message = exception.Message;
        if (!string.IsNullOrEmpty(message))
        {
            // Non-retryable indicators
            if (message.Contains("110004") || message.Contains("110007") || message.Contains("110012") || message.Contains("170131") || message.Contains("175003") || // Insufficient Balance
                message.Contains("10001") || message.Contains("10017") || message.Contains("3400099") || message.Contains("3400150") || message.Contains("110043") || // Invalid Request
                message.Contains("10003") || message.Contains("10004") || message.Contains("10005") || // Authentication Failed
                message.Contains("Authentication Failed") || message.Contains("Invalid API Key") || message.Contains("Permission Denied") ||
                message.Contains("Insufficient Balance") || message.Contains("Invalid Symbol") || message.Contains("Invalid Quantity") || message.Contains("Invalid Price"))
            {
                return ErrorRetryability.NonRetryable;
            }

            // Retryable indicators
            if (message.Contains("10018") || message.Contains("33004") || message.Contains("RateLimited") || message.Contains("Rate limit") || // Rate limited
                message.Contains("10016") || message.Contains("10002") || message.Contains("10010") || message.Contains("3100000") || message.Contains("Unavailable") || // Unavailable
                message.Contains("timeout") || message.Contains("timed out") || message.Contains("connection") || message.Contains("socket"))
            {
                return ErrorRetryability.Retryable;
            }
        }

        return ErrorRetryability.Unknown;
    }
}
