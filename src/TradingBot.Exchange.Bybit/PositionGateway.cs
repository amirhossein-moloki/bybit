using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Models;
using TradingBot.Domain.Enums;
using TradingBot.Exchange.Bybit.Dtos;
using TradingBot.Exchange.Bybit.Services;

namespace TradingBot.Exchange.Bybit;

public class PositionGateway : IPositionGateway
{
    private readonly HttpClient _httpClient;
    private readonly BybitSettings _settings;
    private readonly IResilienceService _resilienceService;
    private readonly ILogger<PositionGateway> _logger;

    public PositionGateway(
        HttpClient httpClient,
        BybitSettings settings,
        IResilienceService resilienceService,
        ILogger<PositionGateway> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_httpClient.BaseAddress == null)
        {
            var baseUrl = string.Equals(_settings.Environment, "Production", StringComparison.OrdinalIgnoreCase)
                ? "https://api.bybit.com"
                : "https://api-testnet.bybit.com";
            _httpClient.BaseAddress = new Uri(baseUrl);
        }
    }

    public async Task<List<ExchangePositionDto>> GetOpenPositionsAsync()
    {
        _logger.LogInformation("BybitPositionQueryStarted: Fetching all open linear positions from Bybit...");

        var queryParams = new Dictionary<string, string>
        {
            { "category", "linear" },
            { "settleCoin", "USDT" }
        };

        try
        {
            var response = await SendPrivateRequestAsync<BybitPositionListResponse>(
                HttpMethod.Get, "/v5/position/list", queryParams, CancellationToken.None);

            if (response == null || response.RetCode != 0 || response.Result == null)
            {
                var code = response?.RetCode ?? -1;
                var msg = response?.RetMsg ?? "Null response";
                _logger.LogWarning("BybitPositionQueryFailed: RetCode={RetCode}, Msg={Msg}", code, msg);
                return new List<ExchangePositionDto>();
            }

            var dtos = new List<ExchangePositionDto>();
            foreach (var info in response.Result.List)
            {
                // Parse size/quantity
                if (decimal.TryParse(info.Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var size) && size > 0)
                {
                    var dto = MapToDto(info);
                    if (dto != null)
                    {
                        dtos.Add(dto);
                    }
                }
            }

            _logger.LogInformation("BybitPositionQueryCompleted: Found {Count} open positions.", dtos.Count);
            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BybitPositionQueryFailed: Exception during GetOpenPositionsAsync.");
            throw;
        }
    }

    public async Task<ExchangePositionDto?> GetPositionAsync(string symbol, PositionSide side)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol cannot be empty.", nameof(symbol));
        }

        _logger.LogInformation("BybitPositionQueryStarted: Fetching position for Symbol={Symbol}, Side={Side}...", symbol, side);

        var queryParams = new Dictionary<string, string>
        {
            { "category", "linear" },
            { "symbol", symbol.ToUpperInvariant() }
        };

        try
        {
            var response = await SendPrivateRequestAsync<BybitPositionListResponse>(
                HttpMethod.Get, "/v5/position/list", queryParams, CancellationToken.None);

            if (response == null || response.RetCode != 0 || response.Result == null)
            {
                var code = response?.RetCode ?? -1;
                var msg = response?.RetMsg ?? "Null response";
                _logger.LogWarning("BybitPositionQueryFailed: RetCode={RetCode}, Msg={Msg}", code, msg);
                return null;
            }

            var expectedSideStr = side == PositionSide.Long ? "Buy" : "Sell";
            var positionInfo = response.Result.List.FirstOrDefault(p =>
                string.Equals(p.Side, expectedSideStr, StringComparison.OrdinalIgnoreCase));

            if (positionInfo == null)
            {
                _logger.LogInformation("BybitPositionQueryCompleted: No position found on Bybit for Symbol={Symbol}, Side={Side}.", symbol, side);
                return null;
            }

            decimal.TryParse(positionInfo.Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var size);
            if (size == 0)
            {
                _logger.LogInformation("BybitPositionQueryCompleted: Position found but size is 0 (closed) for Symbol={Symbol}, Side={Side}.", symbol, side);
                return null;
            }

            return MapToDto(positionInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BybitPositionQueryFailed: Exception during GetPositionAsync.");
            throw;
        }
    }

    private ExchangePositionDto? MapToDto(BybitPositionInfo info)
    {
        if (!Enum.TryParse<PositionSide>(info.Side, true, out var side))
        {
            if (string.Equals(info.Side, "Buy", StringComparison.OrdinalIgnoreCase))
            {
                side = PositionSide.Long;
            }
            else if (string.Equals(info.Side, "Sell", StringComparison.OrdinalIgnoreCase))
            {
                side = PositionSide.Short;
            }
            else
            {
                _logger.LogWarning("BybitPositionQueryWarning: Unknown side '{Side}' for Symbol {Symbol}", info.Side, info.Symbol);
                return null;
            }
        }

        decimal.TryParse(info.Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var size);
        decimal.TryParse(info.AvgPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var avgPrice);
        decimal.TryParse(info.MarkPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var markPrice);
        decimal.TryParse(info.UnrealisedPnl, NumberStyles.Any, CultureInfo.InvariantCulture, out var unrealisedPnl);

        decimal? leverage = null;
        if (decimal.TryParse(info.Leverage, NumberStyles.Any, CultureInfo.InvariantCulture, out var levVal))
        {
            leverage = levVal;
        }

        decimal? margin = null;
        if (decimal.TryParse(info.PositionBalance, NumberStyles.Any, CultureInfo.InvariantCulture, out var margVal))
        {
            margin = margVal;
        }

        decimal? liqPrice = null;
        if (decimal.TryParse(info.LiqPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var liqVal) && liqVal > 0)
        {
            liqPrice = liqVal;
        }

        decimal? stopLoss = null;
        if (decimal.TryParse(info.StopLoss, NumberStyles.Any, CultureInfo.InvariantCulture, out var slVal) && slVal > 0)
        {
            stopLoss = slVal;
        }

        decimal? takeProfit = null;
        if (decimal.TryParse(info.TakeProfit, NumberStyles.Any, CultureInfo.InvariantCulture, out var tpVal) && tpVal > 0)
        {
            takeProfit = tpVal;
        }

        DateTime? updatedAt = null;
        if (long.TryParse(info.UpdatedTime, out var utMs) && utMs > 0)
        {
            updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(utMs).UtcDateTime;
        }

        var exchangeId = $"{info.Symbol}_{side}";

        return new ExchangePositionDto(
            ExchangePositionId: exchangeId,
            Symbol: info.Symbol.ToUpperInvariant(),
            Side: side,
            Quantity: size,
            EntryPrice: avgPrice,
            MarkPrice: markPrice,
            Leverage: leverage,
            Margin: margin,
            UnrealizedPnL: unrealisedPnl,
            LiquidationPrice: liqPrice,
            StopLoss: stopLoss,
            TakeProfit: takeProfit,
            UpdatedAt: updatedAt
        );
    }

    private async Task<BybitResponse<TResult>?> SendPrivateRequestAsync<TResult>(
        HttpMethod method,
        string path,
        IDictionary<string, string> queryParams,
        CancellationToken cancellationToken)
        where TResult : class
    {
        return await _resilienceService.ExecuteHttpAsync(async ct =>
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var recvWindow = _settings.RecvWindow.ToString();
            var apiKey = _settings.ApiKey;
            var apiSecret = _settings.ApiSecret;

            var queryString = string.Join("&", queryParams.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
            var requestPathAndQuery = $"{path}?{queryString}";

            var signature = BybitSignatureGenerator.GenerateSignature(apiSecret, apiKey, timestamp, recvWindow, queryString);

            var request = new HttpRequestMessage(method, requestPathAndQuery);
            request.Headers.Add("X-BAPI-API-KEY", apiKey);
            request.Headers.Add("X-BAPI-SIGN", signature);
            request.Headers.Add("X-BAPI-SIGN-TYPE", "2");
            request.Headers.Add("X-BAPI-TIMESTAMP", timestamp);
            request.Headers.Add("X-BAPI-RECV-WINDOW", recvWindow);

            // Secure logging - NEVER log Secret, Signature, Authentication Headers
            _logger.LogInformation("BybitRequestPrepared: Sending private request. Method={Method}, Path={Path}", method, path);

            var startTime = System.Diagnostics.Stopwatch.StartNew();
            var responseMessage = await _httpClient.SendAsync(request, ct);
            var durationMs = startTime.ElapsedMilliseconds;

            var responseContent = await responseMessage.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("BybitResponseReceived: Received response. StatusCode={StatusCode}, Latency={Latency}ms", responseMessage.StatusCode, durationMs);

            if (!responseMessage.IsSuccessStatusCode)
            {
                _logger.LogError("BybitExecutionFailed: Private Request returned error status code {StatusCode}. Path={Path}", responseMessage.StatusCode, path);
                return new BybitResponse<TResult>
                {
                    RetCode = (int)responseMessage.StatusCode,
                    RetMsg = $"HTTP Error {responseMessage.StatusCode}: {responseContent}"
                };
            }

            var response = JsonSerializer.Deserialize<BybitResponse<TResult>>(responseContent);
            return response;
        }, cancellationToken);
    }
}
