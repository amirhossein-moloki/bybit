using System;
using TradingBot.Application.Enums;

namespace TradingBot.Application.Interfaces;

public interface IErrorClassifier
{
    ErrorRetryability Classify(Exception exception);
}
