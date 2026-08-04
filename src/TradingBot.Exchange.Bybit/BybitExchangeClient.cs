using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Exchange.Bybit.Dtos;
using TradingBot.Exchange.Bybit.Exceptions;
using TradingBot.Exchange.Bybit.Services;

namespace TradingBot.Exchange.Bybit;

public class BybitExchangeClient : IExchangeClient
{
    private readonly HttpClient _httpClient;
    private readonly BybitSettings _settings;
    private readonly IResilienceService _resilienceService;
    private readonly ILogger<BybitExchangeClient> _logger;

    public string ExchangeName => "Bybit";

    public BybitExchangeClient(
        HttpClient httpClient,
        BybitSettings settings,
        IResilienceService resilienceService,
        ILogger<BybitExchangeClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Configure Base Address if not configured externally
        if (_httpClient.BaseAddress == null)
        {
            var baseUrl = _settings.UseSandbox
                ? "https://api-testnet.bybit.com"
                : "https://api.bybit.com";
            _httpClient.BaseAddress = new Uri(baseUrl);
        }
    }

    private string GetApiKey() => _settings.ApiKey;
    private string GetApiSecret() => _settings.ApiSecret;

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("PingAsync: Checking connectivity to Bybit...");
        try
        {
            var response = await _resilienceService.ExecuteHttpAsync(async ct =>
                await _httpClient.GetFromJsonAsync<BybitResponse<BybitServerTime>>(
                    "/v5/market/time", ct), cancellationToken);

            if (response == null || response.RetCode != 0)
            {
                _logger.LogWarning("PingAsync: Received non-zero code or empty response. RetCode={RetCode}, Msg={Msg}",
                    response?.RetCode, response?.RetMsg);
                return false;
            }

            _logger.LogInformation("PingAsync: Connection successful. Bybit Server Time: {TimeSecond}", response.Result?.TimeSecond);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PingAsync: Error connecting to Bybit API.");
            return false;
        }
    }

    public async Task<Order> PlaceOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("PlaceOrderAsync: Placing order {ClientOrderId} for symbol {Symbol}...",
            order.ClientOrderId, order.Symbol.Value);

        // Map domain side to Bybit side
        var sideStr = order.Side == OrderSide.Buy ? "Buy" : "Sell";
        // Map domain order type to Bybit order type
        var orderTypeStr = order.Type == OrderType.Limit ? "Limit" : "Market";

        var payload = new Dictionary<string, object>
        {
            { "category", "spot" },
            { "symbol", order.Symbol.Value },
            { "side", sideStr },
            { "orderType", orderTypeStr },
            { "qty", order.Quantity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "orderLinkId", order.ClientOrderId }
        };

        if (order.Type == OrderType.Limit)
        {
            payload.Add("price", order.Price.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var jsonPayload = JsonSerializer.Serialize(payload);
        var response = await SendPrivateRequestAsync<BybitOrderResult>(
            HttpMethod.Post, "/v5/order/create", jsonPayload, cancellationToken);

        if (response == null || response.Result == null)
        {
            throw new ExchangeException("PlaceOrderAsync: Received empty response from Bybit.");
        }

        _logger.LogInformation("PlaceOrderAsync: Order placed successfully. Bybit OrderId: {OrderId}", response.Result.OrderId);

        // Fetch the fresh status or construct updated order
        var updatedOrder = new Order(
            order.ClientOrderId,
            order.Symbol,
            order.Side,
            order.Type,
            order.Quantity,
            order.Price
        );

        // Mark as Submitted then Accept using the exchange order id
        updatedOrder.Submit();
        updatedOrder.Accept(response.Result.OrderId ?? "BYBIT_PLACEHOLDER_ID");
        return updatedOrder;
    }

    public async Task<Order> GetOrderStatusAsync(string clientOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetOrderStatusAsync: Querying order {ClientOrderId} for symbol {Symbol}...", clientOrderId, symbol);

        var queryParams = new Dictionary<string, string>
        {
            { "category", "spot" },
            { "symbol", symbol.ToUpperInvariant() },
            { "orderLinkId", clientOrderId }
        };

        var response = await SendPrivateRequestAsync<BybitOrderQueryResponse>(
            HttpMethod.Get, "/v5/order/realtime", queryParams, cancellationToken);

        if (response == null || response.Result == null || response.Result.List == null || !response.Result.List.Any())
        {
            _logger.LogWarning("GetOrderStatusAsync: Order {ClientOrderId} not found or empty response.", clientOrderId);
            throw new ExchangeException($"Order with Link ID {clientOrderId} not found on Bybit.");
        }

        var bybitOrder = response.Result.List.First();
        _logger.LogInformation("GetOrderStatusAsync: Order found. Status={Status}, Qty={Qty}, CumExecQty={CumExecQty}",
            bybitOrder.OrderStatus, bybitOrder.Qty, bybitOrder.CumExecQty);

        var orderType = Enum.TryParse<OrderType>(bybitOrder.OrderStatus, true, out var ot) ? ot : OrderType.Limit;
        var side = string.Equals(bybitOrder.Side, "Buy", StringComparison.OrdinalIgnoreCase) ? OrderSide.Buy : OrderSide.Sell;

        decimal.TryParse(bybitOrder.Price, System.Globalization.CultureInfo.InvariantCulture, out var price);
        decimal.TryParse(bybitOrder.Qty, System.Globalization.CultureInfo.InvariantCulture, out var quantity);

        var order = new Order(
            bybitOrder.OrderLinkId,
            new Symbol(bybitOrder.Symbol),
            side,
            orderType,
            new Quantity(quantity),
            new Money(price)
        );

        // Map Bybit OrderStatus to Domain OrderStatus
        var status = MapStatus(bybitOrder.OrderStatus);
        if (status == OrderStatus.Accepted)
        {
            order.Submit();
            order.Accept(bybitOrder.OrderId);
        }
        else
        {
            order.Submit();
            order.Accept(bybitOrder.OrderId);
            order.UpdateStatus(status);
        }

        return order;
    }

    public async Task<decimal> GetAccountBalanceAsync(string coin = "USDT", CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetAccountBalanceAsync: Retrieving balance for coin {Coin}...", coin);

        var queryParams = new Dictionary<string, string>
        {
            { "accountType", "UNIFIED" },
            { "coin", coin.ToUpperInvariant() }
        };

        var response = await SendPrivateRequestAsync<BybitWalletBalanceResponse>(
            HttpMethod.Get, "/v5/account/wallet-balance", queryParams, cancellationToken);

        if (response == null || response.Result == null || response.Result.List == null || !response.Result.List.Any())
        {
            _logger.LogWarning("GetAccountBalanceAsync: No wallet balance found in response.");
            return 0m;
        }

        var account = response.Result.List.FirstOrDefault();
        if (account == null) return 0m;

        var coinBalance = account.Coin.FirstOrDefault(c => string.Equals(c.CoinName, coin, StringComparison.OrdinalIgnoreCase));
        if (coinBalance == null)
        {
            _logger.LogInformation("GetAccountBalanceAsync: Coin {Coin} not found in UNIFIED account balance, defaulting to 0.", coin);
            return 0m;
        }

        if (decimal.TryParse(coinBalance.WalletBalance, System.Globalization.CultureInfo.InvariantCulture, out var balance))
        {
            _logger.LogInformation("GetAccountBalanceAsync: Balance for {Coin} is {Balance}", coin, balance);
            return balance;
        }

        return 0m;
    }

    public async Task<bool> IsSymbolValidAsync(string symbol, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("IsSymbolValidAsync: Checking validity of symbol {Symbol}...", symbol);

        var symbolUpper = symbol.ToUpperInvariant();
        try
        {
            var response = await _resilienceService.ExecuteHttpAsync(async ct =>
                await _httpClient.GetFromJsonAsync<BybitResponse<BybitInstrumentsResponse>>(
                    $"/v5/market/instruments-info?category=spot&symbol={symbolUpper}", ct), cancellationToken);

            if (response == null || response.RetCode != 0 || response.Result == null || response.Result.List == null)
            {
                _logger.LogWarning("IsSymbolValidAsync: Error or empty response for symbol check. RetCode={RetCode}", response?.RetCode);
                return false;
            }

            var instrument = response.Result.List.FirstOrDefault(i => string.Equals(i.Symbol, symbolUpper, StringComparison.OrdinalIgnoreCase));
            var isValid = instrument != null && string.Equals(instrument.Status, "Trading", StringComparison.OrdinalIgnoreCase);

            _logger.LogInformation("IsSymbolValidAsync: Symbol {Symbol} is valid: {IsValid}", symbol, isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IsSymbolValidAsync: Exception during checking symbol validation.");
            return false;
        }
    }

    public async Task<decimal> GetLastPriceAsync(string symbol, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetLastPriceAsync: Querying last price for symbol {Symbol}...", symbol);

        var symbolUpper = symbol.ToUpperInvariant();
        try
        {
            var response = await _resilienceService.ExecuteHttpAsync(async ct =>
                await _httpClient.GetFromJsonAsync<BybitResponse<BybitTickerResponse>>(
                    $"/v5/market/tickers?category=spot&symbol={symbolUpper}", ct), cancellationToken);

            if (response == null || response.RetCode != 0 || response.Result == null || response.Result.List == null || !response.Result.List.Any())
            {
                var errMsg = response?.RetMsg ?? "Unknown error or empty response";
                _logger.LogWarning("GetLastPriceAsync: Received error response from Bybit. Msg={Msg}", errMsg);
                throw new ExchangeException($"GetLastPriceAsync failed for symbol {symbol}: {errMsg}");
            }

            var ticker = response.Result.List.First();
            if (decimal.TryParse(ticker.LastPrice, System.Globalization.CultureInfo.InvariantCulture, out var price))
            {
                _logger.LogInformation("GetLastPriceAsync: Last price for {Symbol} is {Price}", symbolUpper, price);
                return price;
            }

            throw new ExchangeException($"GetLastPriceAsync: Could not parse price for symbol {symbol}. RawValue={ticker.LastPrice}");
        }
        catch (Exception ex) when (ex is not ExchangeException)
        {
            _logger.LogError(ex, "GetLastPriceAsync: Exception while getting last ticker price.");
            throw new ExchangeException($"GetLastPriceAsync failed for symbol {symbol}", ex);
        }
    }

    #region Helper Methods

    private async Task<BybitResponse<T>> SendPrivateRequestAsync<T>(
        HttpMethod method,
        string path,
        object? payloadOrParams,
        CancellationToken cancellationToken)
    {
        return await _resilienceService.ExecuteHttpAsync(async ct =>
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var recvWindow = "5000";
            var apiKey = GetApiKey();
            var apiSecret = GetApiSecret();

            string requestPayload = string.Empty;
            string requestPathAndQuery = path;

            if (method == HttpMethod.Get && payloadOrParams is IDictionary<string, string> queryParams)
            {
                var queryString = string.Join("&", queryParams.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
                requestPayload = queryString;
                requestPathAndQuery = $"{path}?{queryString}";
            }
            else if (method == HttpMethod.Post && payloadOrParams is string jsonBody)
            {
                requestPayload = jsonBody;
            }

            var signature = BybitSignatureGenerator.GenerateSignature(apiSecret, apiKey, timestamp, recvWindow, requestPayload);

            var request = new HttpRequestMessage(method, requestPathAndQuery);
            request.Headers.Add("X-BAPI-API-KEY", apiKey);
            request.Headers.Add("X-BAPI-SIGN", signature);
            request.Headers.Add("X-BAPI-SIGN-TYPE", "2");
            request.Headers.Add("X-BAPI-TIMESTAMP", timestamp);
            request.Headers.Add("X-BAPI-RECV-WINDOW", recvWindow);

            if (method == HttpMethod.Post && !string.IsNullOrEmpty(requestPayload))
            {
                request.Content = new StringContent(requestPayload, Encoding.UTF8, "application/json");
            }

            var responseMessage = await _httpClient.SendAsync(request, ct);
            var responseContent = await responseMessage.Content.ReadAsStringAsync(ct);

            if (!responseMessage.IsSuccessStatusCode)
            {
                _logger.LogError("Bybit Private Request returned error status code {StatusCode}. Content: {Content}",
                    responseMessage.StatusCode, responseContent);
                throw new ExchangeException($"Bybit Private Request failed with status {responseMessage.StatusCode}. Response: {responseContent}");
            }

            var response = JsonSerializer.Deserialize<BybitResponse<T>>(responseContent);
            if (response == null)
            {
                throw new ExchangeException("Bybit Private Request returned null or invalid JSON.");
            }

            if (response.RetCode != 0)
            {
                _logger.LogWarning("Bybit Private Request returned non-zero code. Path={Path}, RetCode={RetCode}, Msg={Msg}",
                    path, response.RetCode, response.RetMsg);
                throw new ExchangeException($"Bybit API Error (RetCode={response.RetCode}): {response.RetMsg}");
            }

            return response;
        }, cancellationToken);
    }

    private OrderStatus MapStatus(string bybitStatus)
    {
        return bybitStatus.ToUpperInvariant() switch
        {
            "NEW" => OrderStatus.Accepted,
            "PARTIALLYFILLED" => OrderStatus.PartiallyFilled,
            "FILLED" => OrderStatus.Filled,
            "CANCELLED" => OrderStatus.Cancelled,
            "REJECTED" => OrderStatus.Rejected,
            _ => OrderStatus.Created
        };
    }

    #endregion
}
