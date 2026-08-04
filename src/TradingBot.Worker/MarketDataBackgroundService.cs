using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces.Streams;

namespace TradingBot.Worker;

public class MarketDataBackgroundService : BackgroundService
{
    private readonly IMarketStream _marketStream;
    private readonly ILogger<MarketDataBackgroundService> _logger;

    public MarketDataBackgroundService(
        IMarketStream marketStream,
        ILogger<MarketDataBackgroundService> logger)
    {
        _marketStream = marketStream ?? throw new ArgumentNullException(nameof(marketStream));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        _logger.LogInformation("MarketDataBackgroundService: Starting...");

        try
        {
            // Subscribe to ticker stream (e.g. BTCUSDT)
            await _marketStream.SubscribeAsync("BTCUSDT", stoppingToken);
            _logger.LogInformation("MarketDataBackgroundService: Subscribed to tickers.BTCUSDT.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarketDataBackgroundService: Failed to subscribe to market data.");
        }

        try
        {
            await foreach (var ticker in _marketStream.ReceiveEventsAsync(stoppingToken))
            {
                _logger.LogInformation("MarketDataBackgroundService: Ticker update received - Symbol: {Symbol}, Price: {Price}, Bid: {BidPrice}, Ask: {AskPrice}, Vol: {Volume}",
                    ticker.Symbol, ticker.Price, ticker.BidPrice, ticker.AskPrice, ticker.Volume);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MarketDataBackgroundService: Cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarketDataBackgroundService: Exception in market event receive loop.");
        }
    }
}
