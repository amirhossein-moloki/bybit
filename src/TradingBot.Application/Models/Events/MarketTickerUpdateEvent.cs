using System;

namespace TradingBot.Application.Models.Events;

public record MarketTickerUpdateEvent(
    string Symbol,
    decimal Price,
    decimal BidPrice,
    decimal AskPrice,
    decimal Volume,
    DateTime Timestamp
);
