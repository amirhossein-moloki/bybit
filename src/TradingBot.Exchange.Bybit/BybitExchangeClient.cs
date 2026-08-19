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
    private readonly IBybitAccountProvider _accountProvider;

    public string ExchangeName => "Bybit";

    public BybitExchangeClient(
        HttpClient httpClient,
        BybitSettings settings,
        IResilienceService resilienceService,
        ILogger<BybitExchangeClient> logger,
        IBybitAccountProvider? accountProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var defaultApiKey = _settings.EffectiveApiKey;
        var defaultApiSecret = _settings.EffectiveApiSecret;
        _accountProvider = accountProvider ?? new SingleBybitAccountProvider(defaultApiKey, defaultApiSecret, settings.Environment);

        if (_httpClient.BaseAddress == null)
        {
            var baseUrl = BybitOptions.GetBaseUrl(_settings.Environment);
            _httpClient.BaseAddress = new Uri(baseUrl);
        }
    }

    private string ResolveBaseUrl(string environment)
    {
        return BybitOptions.GetBaseUrl(environment);
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("PingAsync: Checking connectivity to Bybit for all active accounts...");
        try
        {
            var accounts = await _accountProvider.GetActiveAccountsAsync(cancellationToken);
            if (!accounts.Any())
            {
                _logger.LogWarning("PingAsync: No active Bybit accounts found.");
                return false;
            }

            bool allSuccessful = true;
            foreach (var account in accounts)
            {
                var baseUrl = ResolveBaseUrl(account.Environment);
                var requestUrl = new Uri(new Uri(baseUrl), "/v5/market/time");

                var response = await _resilienceService.ExecuteHttpAsync(async ct =>
                    await _httpClient.GetFromJsonAsync<BybitResponse<BybitServerTime>>(requestUrl, ct), cancellationToken);

                if (response == null || response.RetCode != 0)
                {
                    _logger.LogWarning("PingAsync: Account {Name} received non-zero code or empty response. RetCode={RetCode}, Msg={Msg}",
                        account.Name, response?.RetCode, response?.RetMsg);
                    allSuccessful = false;
                }
                else
                {
                    _logger.LogInformation("PingAsync: Connection successful for {Name}. Bybit Server Time: {TimeSecond}",
                        account.Name, response.Result?.TimeSecond);
                }
            }

            return allSuccessful;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PingAsync: Error connecting to Bybit API.");
            return false;
        }
    }

    public async Task<Order> PlaceOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("PlaceOrderAsync: Placing order {ClientOrderId} for symbol {Symbol} across all active accounts...",
            order.ClientOrderId, order.Symbol.Value);

        var accounts = await _accountProvider.GetActiveAccountsAsync(cancellationToken);
        if (!accounts.Any())
        {
            throw new ExchangeException("No active Bybit accounts configured.");
        }

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
        Order? primaryOrder = null;

        foreach (var account in accounts)
        {
            try
            {
                var response = await SendPrivateRequestAsync<BybitOrderResult>(
                    account, HttpMethod.Post, "/v5/order/create", jsonPayload, cancellationToken);

                if (response == null || response.Result == null)
                {
                    throw new ExchangeException($"PlaceOrderAsync: Received empty response from Bybit for account {account.Name}.");
                }

                _logger.LogInformation("PlaceOrderAsync: Order placed successfully on account {Name}. Bybit OrderId: {OrderId}",
                    account.Name, response.Result.OrderId);

                if (primaryOrder == null)
                {
                    // Fetch the fresh status or construct updated order based on the first successful account
                    primaryOrder = new Order(
                        order.ClientOrderId,
                        order.Symbol,
                        order.Side,
                        order.Type,
                        order.Quantity,
                        order.Price
                    );
                    primaryOrder.Submit();
                    primaryOrder.Accept(response.Result.OrderId ?? "BYBIT_PLACEHOLDER_ID");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PlaceOrderAsync: Failed to place order on account {Name}", account.Name);
                if (account == accounts.First())
                {
                    // If the first/primary account fails, throw the exception
                    throw;
                }
            }
        }

        return primaryOrder ?? throw new ExchangeException("Failed to place order on any configured Bybit accounts.");
    }

    public async Task<Order> GetOrderStatusAsync(string clientOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetOrderStatusAsync: Querying order {ClientOrderId} for symbol {Symbol}...", clientOrderId, symbol);

        var accounts = await _accountProvider.GetActiveAccountsAsync(cancellationToken);
        if (!accounts.Any())
        {
            throw new ExchangeException("No active Bybit accounts configured.");
        }

        var queryParams = new Dictionary<string, string>
        {
            { "category", "spot" },
            { "symbol", symbol.ToUpperInvariant() },
            { "orderLinkId", clientOrderId }
        };

        foreach (var account in accounts)
        {
            try
            {
                var response = await SendPrivateRequestAsync<BybitOrderQueryResponse>(
                    account, HttpMethod.Get, "/v5/order/realtime", queryParams, cancellationToken);

                if (response == null || response.Result == null || response.Result.List == null || !response.Result.List.Any())
                {
                    continue;
                }

                var bybitOrder = response.Result.List.First();
                _logger.LogInformation("GetOrderStatusAsync: Order found on account {Name}. Status={Status}, Qty={Qty}",
                    account.Name, bybitOrder.OrderStatus, bybitOrder.Qty);

                var orderType = Enum.TryParse<OrderType>(bybitOrder.OrderStatus, true, out var ot) ? ot : OrderType.Limit;
                var side = string.Equals(bybitOrder.Side, "Buy", StringComparison.OrdinalIgnoreCase) ? OrderSide.Buy : OrderSide.Sell;

                decimal.TryParse(bybitOrder.Price, System.Globalization.CultureInfo.InvariantCulture, out var price);
                decimal.TryParse(bybitOrder.Qty, System.Globalization.CultureInfo.InvariantCulture, out var quantity);

                var order = new Order(
                    bybitOrder.OrderLinkId,
                    new TradingBot.Domain.ValueObjects.Symbol(bybitOrder.Symbol),
                    side,
                    orderType,
                    new Quantity(quantity),
                    new Money(price)
                );

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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetOrderStatusAsync: Failed to query order on account {Name}", account.Name);
            }
        }

        throw new ExchangeException($"Order with Link ID {clientOrderId} not found on any configured Bybit accounts.");
    }

    public async Task<decimal> GetAccountBalanceAsync(string coin = "USDT", CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetAccountBalanceAsync: Retrieving balance for coin {Coin} across all active accounts...", coin);

        var accounts = await _accountProvider.GetActiveAccountsAsync(cancellationToken);
        if (!accounts.Any())
        {
            return 0m;
        }

        var queryParams = new Dictionary<string, string>
        {
            { "accountType", "UNIFIED" },
            { "coin", coin.ToUpperInvariant() }
        };

        decimal totalBalance = 0m;
        bool anySuccess = false;

        foreach (var account in accounts)
        {
            try
            {
                var response = await SendPrivateRequestAsync<BybitWalletBalanceResponse>(
                    account, HttpMethod.Get, "/v5/account/wallet-balance", queryParams, cancellationToken);

                if (response == null || response.Result == null || response.Result.List == null || !response.Result.List.Any())
                {
                    continue;
                }

                var walletInfo = response.Result.List.FirstOrDefault();
                if (walletInfo == null) continue;

                var coinBalance = walletInfo.Coin.FirstOrDefault(c => string.Equals(c.CoinName, coin, StringComparison.OrdinalIgnoreCase));
                if (coinBalance != null && decimal.TryParse(coinBalance.WalletBalance, System.Globalization.CultureInfo.InvariantCulture, out var balance))
                {
                    _logger.LogInformation("GetAccountBalanceAsync: Balance for account {Name} is {Balance}", account.Name, balance);
                    totalBalance += balance;
                    anySuccess = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetAccountBalanceAsync: Failed to retrieve balance for account {Name}", account.Name);
            }
        }

        return anySuccess ? totalBalance : 0m;
    }

    public async Task<bool> IsSymbolValidAsync(string symbol, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("IsSymbolValidAsync: Checking validity of symbol {Symbol}...", symbol);

        var accounts = await _accountProvider.GetActiveAccountsAsync(cancellationToken);
        if (!accounts.Any())
        {
            return false;
        }

        var symbolUpper = symbol.ToUpperInvariant();
        var primaryAccount = accounts.First();
        var baseUrl = ResolveBaseUrl(primaryAccount.Environment);
        var requestUrl = new Uri(new Uri(baseUrl), $"/v5/market/instruments-info?category=spot&symbol={symbolUpper}");

        try
        {
            var response = await _resilienceService.ExecuteHttpAsync(async ct =>
                await _httpClient.GetFromJsonAsync<BybitResponse<BybitInstrumentsResponse>>(requestUrl, ct), cancellationToken);

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

        var accounts = await _accountProvider.GetActiveAccountsAsync(cancellationToken);
        if (!accounts.Any())
        {
            throw new ExchangeException("No active Bybit accounts configured.");
        }

        var symbolUpper = symbol.ToUpperInvariant();
        var primaryAccount = accounts.First();
        var baseUrl = ResolveBaseUrl(primaryAccount.Environment);
        var requestUrl = new Uri(new Uri(baseUrl), $"/v5/market/tickers?category=spot&symbol={symbolUpper}");

        try
        {
            var response = await _resilienceService.ExecuteHttpAsync(async ct =>
                await _httpClient.GetFromJsonAsync<BybitResponse<BybitTickerResponse>>(requestUrl, ct), cancellationToken);

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
        BybitAccountInfo account,
        HttpMethod method,
        string path,
        object? payloadOrParams,
        CancellationToken cancellationToken)
    {
        Func<Exception, bool>? isRetryable = null;

        // Detect non-idempotent endpoints (such as order creation)
        if (path.Contains("/order/create") ||
            path.Contains("/position/set-trading-stop") ||
            path.Contains("/position/close") ||
            path.Contains("/order/cancel"))
        {
            // Do NOT blindly retry non-idempotent operations
            isRetryable = ex => false;
        }

        return await _resilienceService.ExecuteHttpAsync(async ct =>
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var recvWindow = "5000";
            var apiKey = account.ApiKey;
            var apiSecret = account.ApiSecret;

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

            var baseUrl = ResolveBaseUrl(account.Environment);
            var requestUrl = new Uri(new Uri(baseUrl), requestPathAndQuery);

            var request = new HttpRequestMessage(method, requestUrl);
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
                _logger.LogError("Bybit Private Request returned error status code {StatusCode} for account {AccountName}. Content: {Content}",
                    responseMessage.StatusCode, account.Name, responseContent);
                throw new ExchangeException($"Bybit Private Request failed with status {responseMessage.StatusCode}. Response: {responseContent}");
            }

            var response = JsonSerializer.Deserialize<BybitResponse<T>>(responseContent);
            if (response == null)
            {
                throw new ExchangeException("Bybit Private Request returned null or invalid JSON.");
            }

            if (response.RetCode != 0)
            {
                _logger.LogWarning("Bybit Private Request returned non-zero code for account {AccountName}. Path={Path}, RetCode={RetCode}, Msg={Msg}",
                    account.Name, path, response.RetCode, response.RetMsg);
                throw new ExchangeException($"Bybit API Error (RetCode={response.RetCode}): {response.RetMsg}");
            }

            return response;
        }, isRetryable, cancellationToken);
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
