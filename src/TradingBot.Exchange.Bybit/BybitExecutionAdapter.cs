using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Enums;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Enums;
using TradingBot.Exchange.Bybit.Dtos;
using TradingBot.Exchange.Bybit.Services;

namespace TradingBot.Exchange.Bybit;

public class BybitExecutionAdapter : IExchangeTradingGateway
{
    private readonly HttpClient _httpClient;
    private readonly BybitSettings _settings;
    private readonly IResilienceService _resilienceService;
    private readonly ILogger<BybitExecutionAdapter> _logger;
    private readonly IBybitAccountProvider _accountProvider;

    public BybitExecutionAdapter(
        HttpClient httpClient,
        BybitSettings settings,
        IResilienceService resilienceService,
        ILogger<BybitExecutionAdapter> logger,
        IBybitAccountProvider? accountProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _accountProvider = accountProvider ?? new SingleBybitAccountProvider(settings.ApiKey, settings.ApiSecret, settings.Environment);

        if (_httpClient.BaseAddress == null)
        {
            var baseUrl = ResolveBaseUrl(_settings.Environment);
            _httpClient.BaseAddress = new Uri(baseUrl);
        }
    }

    private string ResolveBaseUrl(string environment)
    {
        if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.bybit.com";
        }
        if (string.Equals(environment, "Demo", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-demo.bybit.com";
        }
        return "https://api-testnet.bybit.com";
    }

    public async Task<OrderResult> CreateOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        _logger.LogInformation("BybitExecutionRequested: Creating linear order for Symbol={Symbol}, Side={Side}, Type={Type}, Quantity={Quantity} across all active accounts...",
            request.Symbol, request.Side, request.Type, request.Quantity);

        var accounts = await _accountProvider.GetActiveAccountsAsync(cancellationToken);
        if (!accounts.Any())
        {
            return new OrderResult
            {
                Success = false,
                Status = OrderStatus.Failed,
                ErrorMessage = "No active Bybit accounts configured.",
                ErrorCode = "NO_ACTIVE_ACCOUNTS",
                ErrorType = ExchangeErrorType.Unavailable
            };
        }

        var sideStr = request.Side == OrderSide.Buy ? "Buy" : "Sell";
        var orderTypeStr = request.Type == OrderType.Limit ? "Limit" : "Market";

        var payload = new Dictionary<string, object>
        {
            { "category", "linear" },
            { "symbol", request.Symbol.ToUpperInvariant() },
            { "side", sideStr },
            { "orderType", orderTypeStr },
            { "qty", request.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "orderLinkId", request.ClientOrderId }
        };

        if (request.Type == OrderType.Limit)
        {
            payload.Add("price", request.Price.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (request.ReduceOnly)
        {
            payload.Add("reduceOnly", true);
        }

        var results = new List<OrderResult>();

        foreach (var account in accounts)
        {
            try
            {
                var response = await SendPrivateRequestAsync<BybitOrderResult>(
                    account, HttpMethod.Post, "/v5/order/create", payload, cancellationToken);

                if (response == null)
                {
                    _logger.LogError("BybitExecutionFailed: Received null response from Bybit Create Order API for account {Account}.", account.Name);
                    results.Add(new OrderResult
                    {
                        Success = false,
                        Status = OrderStatus.Failed,
                        ErrorMessage = $"Null response from exchange for account {account.Name}.",
                        ErrorCode = "NULL_RESPONSE",
                        ErrorType = ExchangeErrorType.Unavailable
                    });
                    continue;
                }

                if (response.RetCode != 0)
                {
                    var errorType = MapBybitErrorCode(response.RetCode);
                    _logger.LogWarning("BybitExecutionFailed: Create Order API returned non-zero code for account {Account}. RetCode={RetCode}, Msg={Msg}",
                        account.Name, response.RetCode, response.RetMsg);

                    results.Add(new OrderResult
                    {
                        Success = false,
                        Status = OrderStatus.Rejected,
                        ErrorMessage = response.RetMsg,
                        ErrorCode = response.RetCode.ToString(),
                        ErrorType = errorType
                    });
                    continue;
                }

                _logger.LogInformation("BybitOrderCreated: Linear order created successfully on account {Account}. Symbol={Symbol}, Side={Side}, Qty={Quantity}, ExchangeOrderId={ExchangeOrderId}",
                    account.Name, request.Symbol, sideStr, request.Quantity, response.Result?.OrderId);

                results.Add(new OrderResult
                {
                    Success = true,
                    ExchangeOrderId = response.Result?.OrderId,
                    Status = OrderStatus.New,
                    ErrorMessage = "Order created successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BybitExecutionFailed: Exception during CreateOrderAsync on account {Account}.", account.Name);
                results.Add(new OrderResult
                {
                    Success = false,
                    Status = OrderStatus.Failed,
                    ErrorMessage = ex.Message,
                    ErrorCode = "EXCEPTION",
                    ErrorType = ExchangeErrorType.Unknown
                });
            }
        }

        // Return the first successful result, or the first failure if all failed
        var firstSuccess = results.FirstOrDefault(r => r.Success);
        if (firstSuccess != null)
        {
            return firstSuccess;
        }

        return results.FirstOrDefault() ?? new OrderResult
        {
            Success = false,
            Status = OrderStatus.Failed,
            ErrorMessage = "All submissions failed.",
            ErrorCode = "ALL_FAILED",
            ErrorType = ExchangeErrorType.Unknown
        };
    }

    public async Task<OrderResult> GetOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(exchangeOrderId)) throw new ArgumentException("Exchange Order ID cannot be null or empty.", nameof(exchangeOrderId));
        if (string.IsNullOrEmpty(symbol)) throw new ArgumentException("Symbol cannot be null or empty.", nameof(symbol));

        _logger.LogInformation("BybitOrderQueryStarted: Querying order details across all active accounts. ExchangeOrderId={ExchangeOrderId}, Symbol={Symbol}",
            exchangeOrderId, symbol);

        var accounts = await _accountProvider.GetActiveAccountsAsync(cancellationToken);
        if (!accounts.Any())
        {
            return new OrderResult
            {
                Success = false,
                Status = OrderStatus.Failed,
                ErrorMessage = "No active Bybit accounts configured.",
                ErrorCode = "NO_ACTIVE_ACCOUNTS",
                ErrorType = ExchangeErrorType.Unavailable
            };
        }

        var queryParams = new Dictionary<string, string>
        {
            { "category", "linear" },
            { "symbol", symbol.ToUpperInvariant() }
        };

        if (exchangeOrderId.StartsWith("TB-", StringComparison.OrdinalIgnoreCase) || exchangeOrderId.StartsWith("BOT-", StringComparison.OrdinalIgnoreCase))
        {
            queryParams.Add("orderLinkId", exchangeOrderId);
        }
        else
        {
            queryParams.Add("orderId", exchangeOrderId);
        }

        foreach (var account in accounts)
        {
            try
            {
                var response = await SendPrivateRequestAsync<BybitOrderQueryResponse>(
                    account, HttpMethod.Get, "/v5/order/realtime", queryParams, cancellationToken);

                if (response == null || response.RetCode != 0)
                {
                    continue;
                }

                var orderInfo = response.Result?.List?.FirstOrDefault();
                if (orderInfo == null)
                {
                    continue;
                }

                var internalStatus = MapBybitStatus(orderInfo.OrderStatus);

                decimal.TryParse(orderInfo.CumExecQty, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cumExecQty);
                decimal.TryParse(orderInfo.AvgPrice, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var avgPrice);
                decimal.TryParse(orderInfo.Qty, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var origQty);

                if (avgPrice == 0 && !string.IsNullOrEmpty(orderInfo.Price))
                {
                    decimal.TryParse(orderInfo.Price, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out avgPrice);
                }

                var remainingQty = Math.Max(0m, origQty - cumExecQty);

                _logger.LogInformation("BybitOrderQueryCompleted: Order query completed for account {Account}. ExchangeOrderId={ExchangeOrderId}, Status={Status}, ExecQty={ExecQty}",
                    account.Name, exchangeOrderId, internalStatus, cumExecQty);

                return new OrderResult
                {
                    Success = true,
                    ExchangeOrderId = orderInfo.OrderId,
                    Status = internalStatus,
                    ErrorMessage = "Order queried successfully.",
                    ExecutedQuantity = cumExecQty,
                    ExecutedPrice = avgPrice,
                    RemainingQuantity = remainingQty
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BybitExecutionFailed: Exception during GetOrderAsync on account {Account}.", account.Name);
            }
        }

        return new OrderResult
        {
            Success = false,
            Status = OrderStatus.Failed,
            ErrorMessage = "Order not found on any active configured Bybit accounts.",
            ErrorCode = "ORDER_NOT_FOUND",
            ErrorType = ExchangeErrorType.InvalidRequest
        };
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(exchangeOrderId)) throw new ArgumentException("Exchange Order ID cannot be null or empty.", nameof(exchangeOrderId));
        if (string.IsNullOrEmpty(symbol)) throw new ArgumentException("Symbol cannot be null or empty.", nameof(symbol));

        _logger.LogInformation("BybitOrderCancelled: Initializing cancellation across all active accounts. ExchangeOrderId={ExchangeOrderId}, Symbol={Symbol}",
            exchangeOrderId, symbol);

        var accounts = await _accountProvider.GetActiveAccountsAsync(cancellationToken);
        if (!accounts.Any())
        {
            return new OrderResult
            {
                Success = false,
                Status = OrderStatus.Failed,
                ErrorMessage = "No active Bybit accounts configured.",
                ErrorCode = "NO_ACTIVE_ACCOUNTS",
                ErrorType = ExchangeErrorType.Unavailable
            };
        }

        var payload = new Dictionary<string, object>
        {
            { "category", "linear" },
            { "symbol", symbol.ToUpperInvariant() },
            { "orderId", exchangeOrderId }
        };

        var results = new List<OrderResult>();

        foreach (var account in accounts)
        {
            try
            {
                var response = await SendPrivateRequestAsync<BybitOrderResult>(
                    account, HttpMethod.Post, "/v5/order/cancel", payload, cancellationToken);

                if (response == null || response.RetCode != 0)
                {
                    _logger.LogWarning("BybitExecutionFailed: Cancel failed or not found on account {Account}.", account.Name);
                    results.Add(new OrderResult
                    {
                        Success = false,
                        Status = OrderStatus.Failed,
                        ErrorMessage = response?.RetMsg ?? "Failed response.",
                        ErrorCode = response?.RetCode.ToString() ?? "ERROR",
                        ErrorType = ExchangeErrorType.Unknown
                    });
                    continue;
                }

                _logger.LogInformation("BybitOrderCancelled: Order cancelled successfully on account {Account}. ExchangeOrderId={ExchangeOrderId}",
                    account.Name, exchangeOrderId);

                results.Add(new OrderResult
                {
                    Success = true,
                    ExchangeOrderId = response.Result?.OrderId ?? exchangeOrderId,
                    Status = OrderStatus.Cancelled,
                    ErrorMessage = "Order cancelled successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BybitExecutionFailed: Exception during CancelOrderAsync on account {Account}.", account.Name);
                results.Add(new OrderResult
                {
                    Success = false,
                    Status = OrderStatus.Failed,
                    ErrorMessage = ex.Message,
                    ErrorCode = "EXCEPTION",
                    ErrorType = ExchangeErrorType.Unknown
                });
            }
        }

        var firstSuccess = results.FirstOrDefault(r => r.Success);
        if (firstSuccess != null)
        {
            return firstSuccess;
        }

        return results.FirstOrDefault() ?? new OrderResult
        {
            Success = false,
            Status = OrderStatus.Failed,
            ErrorMessage = "All cancellations failed.",
            ErrorCode = "ALL_FAILED",
            ErrorType = ExchangeErrorType.Unknown
        };
    }

    private async Task<BybitResponse<TResult>?> SendPrivateRequestAsync<TResult>(
        BybitAccountInfo account,
        HttpMethod method,
        string path,
        object? payloadOrParams,
        CancellationToken cancellationToken)
        where TResult : class
    {
        Func<Exception, bool>? isRetryable = null;

        // Detect non-idempotent endpoints (such as order creation, trading stops, position closure)
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
            var recvWindow = _settings.RecvWindow.ToString();
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
            else if (method == HttpMethod.Post && payloadOrParams != null)
            {
                if (payloadOrParams is string jsonStr)
                {
                    requestPayload = jsonStr;
                }
                else
                {
                    requestPayload = JsonSerializer.Serialize(payloadOrParams);
                }
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

            // Secure logging - NEVER log Secret, Signature, Authentication Headers, or Telegram Session
            _logger.LogInformation("BybitRequestPrepared: Sending private request for account {AccountName}. Method={Method}, Path={Path}", account.Name, method, path);

            var startTime = System.Diagnostics.Stopwatch.StartNew();
            var responseMessage = await _httpClient.SendAsync(request, ct);
            var durationMs = startTime.ElapsedMilliseconds;

            var responseContent = await responseMessage.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("BybitResponseReceived: Received response for account {AccountName}. StatusCode={StatusCode}, Latency={Latency}ms", account.Name, responseMessage.StatusCode, durationMs);

            if (!responseMessage.IsSuccessStatusCode)
            {
                _logger.LogError("BybitExecutionFailed: Private Request returned error status code {StatusCode} for account {AccountName}. Path={Path}", responseMessage.StatusCode, account.Name, path);
                return new BybitResponse<TResult>
                {
                    RetCode = (int)responseMessage.StatusCode,
                    RetMsg = $"HTTP Error {responseMessage.StatusCode}: {responseContent}"
                };
            }

            var response = JsonSerializer.Deserialize<BybitResponse<TResult>>(responseContent);
            return response;
        }, isRetryable, cancellationToken);
    }

    public async Task<OrderResult> SetTradingStopAsync(
        string symbol,
        OrderSide side,
        decimal? stopLoss,
        decimal? takeProfit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(symbol)) throw new ArgumentException("Symbol cannot be null or empty.", nameof(symbol));

        _logger.LogInformation("BybitSetTradingStopRequested: Setting SL/TP across all active accounts. Symbol={Symbol}, Side={Side}, SL={StopLoss}, TP={TakeProfit}",
            symbol, side, stopLoss, takeProfit);

        var accounts = await _accountProvider.GetActiveAccountsAsync(cancellationToken);
        if (!accounts.Any())
        {
            return new OrderResult
            {
                Success = false,
                Status = OrderStatus.Failed,
                ErrorMessage = "No active Bybit accounts configured.",
                ErrorCode = "NO_ACTIVE_ACCOUNTS",
                ErrorType = ExchangeErrorType.Unavailable
            };
        }

        var payload = new Dictionary<string, object>
        {
            { "category", "linear" },
            { "symbol", symbol.ToUpperInvariant() },
            { "positionIdx", 0 }
        };

        payload.Add("stopLoss", stopLoss.HasValue
            ? stopLoss.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "0");

        payload.Add("takeProfit", takeProfit.HasValue
            ? takeProfit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "0");

        if (stopLoss.HasValue)
        {
            payload.Add("slTriggerBy", "LastPrice");
        }
        if (takeProfit.HasValue)
        {
            payload.Add("tpTriggerBy", "LastPrice");
        }

        var results = new List<OrderResult>();

        foreach (var account in accounts)
        {
            try
            {
                var response = await SendPrivateRequestAsync<BybitOrderResult>(
                    account, HttpMethod.Post, "/v5/position/set-trading-stop", payload, cancellationToken);

                if (response == null || response.RetCode != 0)
                {
                    _logger.LogWarning("BybitExecutionFailed: Set Trading Stop API returned error code for account {Account}. RetCode={RetCode}, Msg={Msg}",
                        account.Name, response?.RetCode, response?.RetMsg);

                    results.Add(new OrderResult
                    {
                        Success = false,
                        Status = OrderStatus.Rejected,
                        ErrorMessage = response?.RetMsg ?? "Failed.",
                        ErrorCode = response?.RetCode.ToString() ?? "ERROR",
                        ErrorType = ExchangeErrorType.Unknown
                    });
                    continue;
                }

                _logger.LogInformation("BybitTradingStopSet: Stop parameters updated successfully on account {Account}. Symbol={Symbol}, SL={StopLoss}, TP={TakeProfit}",
                    account.Name, symbol, stopLoss, takeProfit);

                results.Add(new OrderResult
                {
                    Success = true,
                    Status = OrderStatus.Filled,
                    ErrorMessage = "Trading stop set successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BybitExecutionFailed: Exception during SetTradingStopAsync on account {Account}.", account.Name);
                results.Add(new OrderResult
                {
                    Success = false,
                    Status = OrderStatus.Failed,
                    ErrorMessage = ex.Message,
                    ErrorCode = "EXCEPTION",
                    ErrorType = ExchangeErrorType.Unknown
                });
            }
        }

        var firstSuccess = results.FirstOrDefault(r => r.Success);
        if (firstSuccess != null)
        {
            return firstSuccess;
        }

        return results.FirstOrDefault() ?? new OrderResult
        {
            Success = false,
            Status = OrderStatus.Failed,
            ErrorMessage = "All SetTradingStop operations failed.",
            ErrorCode = "ALL_FAILED",
            ErrorType = ExchangeErrorType.Unknown
        };
    }

    public static ExchangeErrorType MapBybitErrorCode(int retCode)
    {
        return retCode switch
        {
            10001 or 10017 or 3400099 or 3400150 or 110043 => ExchangeErrorType.InvalidRequest,
            10003 or 10004 or 10005 => ExchangeErrorType.AuthenticationFailed,
            10018 or 33004 => ExchangeErrorType.RateLimited,
            110004 or 110007 or 110012 or 170131 or 175003 => ExchangeErrorType.InsufficientBalance,
            10016 or 10002 or 10010 or 3100000 => ExchangeErrorType.Unavailable,
            _ => ExchangeErrorType.Unknown
        };
    }

    public static OrderStatus MapBybitStatus(string? bybitStatus)
    {
        if (string.IsNullOrEmpty(bybitStatus))
        {
            return OrderStatus.Unknown;
        }

        return bybitStatus.ToUpperInvariant() switch
        {
            "CREATED" => OrderStatus.Created,
            "SUBMITTED" => OrderStatus.Submitted,
            "NEW" => OrderStatus.New,
            "PARTIALLYFILLED" => OrderStatus.PartiallyFilled,
            "FILLED" => OrderStatus.Filled,
            "CANCELLED" => OrderStatus.Cancelled,
            "REJECTED" => OrderStatus.Rejected,
            "FAILED" => OrderStatus.Failed,
            "PENDING" => OrderStatus.Pending,
            "TRIGGERED" => OrderStatus.Pending,
            "UNTRIGGERED" => OrderStatus.Pending,
            "DEACTIVATED" => OrderStatus.Cancelled,
            _ => OrderStatus.Unknown
        };
    }

}
