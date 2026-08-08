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

    public BybitExecutionAdapter(
        HttpClient httpClient,
        BybitSettings settings,
        IResilienceService resilienceService,
        ILogger<BybitExecutionAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_httpClient.BaseAddress == null)
        {
            var baseUrl = ResolveBaseUrl();
            _httpClient.BaseAddress = new Uri(baseUrl);
        }
    }

    private string ResolveBaseUrl()
    {
        if (string.Equals(_settings.Environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.bybit.com";
        }
        return "https://api-testnet.bybit.com";
    }

    public async Task<OrderResult> CreateOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        _logger.LogInformation("BybitExecutionRequested: Creating linear order for Symbol={Symbol}, Side={Side}, Type={Type}, Quantity={Quantity}",
            request.Symbol, request.Side, request.Type, request.Quantity);

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

        try
        {
            var response = await SendPrivateRequestAsync<BybitOrderResult>(
                HttpMethod.Post, "/v5/order/create", payload, cancellationToken);

            if (response == null)
            {
                _logger.LogError("BybitExecutionFailed: Received null response from Bybit Create Order API.");
                return new OrderResult
                {
                    Success = false,
                    Status = OrderStatus.Failed,
                    ErrorMessage = "Null response from exchange.",
                    ErrorCode = "NULL_RESPONSE",
                    ErrorType = ExchangeErrorType.Unavailable
                };
            }

            if (response.RetCode != 0)
            {
                var errorType = MapBybitErrorCode(response.RetCode);
                _logger.LogWarning("BybitExecutionFailed: Create Order API returned non-zero code. RetCode={RetCode}, Msg={Msg}",
                    response.RetCode, response.RetMsg);

                return new OrderResult
                {
                    Success = false,
                    Status = OrderStatus.Rejected,
                    ErrorMessage = response.RetMsg,
                    ErrorCode = response.RetCode.ToString(),
                    ErrorType = errorType
                };
            }

            _logger.LogInformation("BybitOrderCreated: Linear order created successfully. Symbol={Symbol}, Side={Side}, Qty={Quantity}, ExchangeOrderId={ExchangeOrderId}",
                request.Symbol, sideStr, request.Quantity, response.Result?.OrderId);

            return new OrderResult
            {
                Success = true,
                ExchangeOrderId = response.Result?.OrderId,
                Status = OrderStatus.New,
                ErrorMessage = "Order created successfully."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BybitExecutionFailed: Exception during CreateOrderAsync.");
            return new OrderResult
            {
                Success = false,
                Status = OrderStatus.Failed,
                ErrorMessage = ex.Message,
                ErrorCode = "EXCEPTION",
                ErrorType = ExchangeErrorType.Unknown
            };
        }
    }

    public async Task<OrderResult> GetOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(exchangeOrderId)) throw new ArgumentException("Exchange Order ID cannot be null or empty.", nameof(exchangeOrderId));
        if (string.IsNullOrEmpty(symbol)) throw new ArgumentException("Symbol cannot be null or empty.", nameof(symbol));

        _logger.LogInformation("BybitOrderQueryStarted: Querying order details. ExchangeOrderId={ExchangeOrderId}, Symbol={Symbol}",
            exchangeOrderId, symbol);

        var queryParams = new Dictionary<string, string>
        {
            { "category", "linear" },
            { "symbol", symbol.ToUpperInvariant() },
            { "orderId", exchangeOrderId }
        };

        try
        {
            var response = await SendPrivateRequestAsync<BybitOrderQueryResponse>(
                HttpMethod.Get, "/v5/order/realtime", queryParams, cancellationToken);

            if (response == null)
            {
                _logger.LogError("BybitExecutionFailed: Received null response from Bybit Order Query API.");
                return new OrderResult
                {
                    Success = false,
                    Status = OrderStatus.Failed,
                    ErrorMessage = "Null response from exchange.",
                    ErrorCode = "NULL_RESPONSE",
                    ErrorType = ExchangeErrorType.Unavailable
                };
            }

            if (response.RetCode != 0)
            {
                var errorType = MapBybitErrorCode(response.RetCode);
                _logger.LogWarning("BybitExecutionFailed: Order Query API returned non-zero code. RetCode={RetCode}, Msg={Msg}",
                    response.RetCode, response.RetMsg);

                return new OrderResult
                {
                    Success = false,
                    Status = OrderStatus.Failed,
                    ErrorMessage = response.RetMsg,
                    ErrorCode = response.RetCode.ToString(),
                    ErrorType = errorType
                };
            }

            var orderInfo = response.Result?.List?.FirstOrDefault();
            if (orderInfo == null)
            {
                _logger.LogWarning("BybitExecutionFailed: Order {ExchangeOrderId} not found in query result list.", exchangeOrderId);
                return new OrderResult
                {
                    Success = false,
                    Status = OrderStatus.Failed,
                    ErrorMessage = "Order not found in exchange response.",
                    ErrorCode = "ORDER_NOT_FOUND",
                    ErrorType = ExchangeErrorType.InvalidRequest
                };
            }

            var internalStatus = MapBybitStatus(orderInfo.OrderStatus);

            _logger.LogInformation("BybitOrderQueryCompleted: Order query completed. ExchangeOrderId={ExchangeOrderId}, Status={Status}",
                exchangeOrderId, internalStatus);

            return new OrderResult
            {
                Success = true,
                ExchangeOrderId = orderInfo.OrderId,
                Status = internalStatus,
                ErrorMessage = "Order queried successfully."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BybitExecutionFailed: Exception during GetOrderAsync.");
            return new OrderResult
            {
                Success = false,
                Status = OrderStatus.Failed,
                ErrorMessage = ex.Message,
                ErrorCode = "EXCEPTION",
                ErrorType = ExchangeErrorType.Unknown
            };
        }
    }

    public async Task<OrderResult> CancelOrderAsync(string exchangeOrderId, string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(exchangeOrderId)) throw new ArgumentException("Exchange Order ID cannot be null or empty.", nameof(exchangeOrderId));
        if (string.IsNullOrEmpty(symbol)) throw new ArgumentException("Symbol cannot be null or empty.", nameof(symbol));

        _logger.LogInformation("BybitOrderCancelled: Initializing cancellation. ExchangeOrderId={ExchangeOrderId}, Symbol={Symbol}",
            exchangeOrderId, symbol);

        var payload = new Dictionary<string, object>
        {
            { "category", "linear" },
            { "symbol", symbol.ToUpperInvariant() },
            { "orderId", exchangeOrderId }
        };

        try
        {
            var response = await SendPrivateRequestAsync<BybitOrderResult>(
                HttpMethod.Post, "/v5/order/cancel", payload, cancellationToken);

            if (response == null)
            {
                _logger.LogError("BybitExecutionFailed: Received null response from Bybit Cancel Order API.");
                return new OrderResult
                {
                    Success = false,
                    Status = OrderStatus.Failed,
                    ErrorMessage = "Null response from exchange.",
                    ErrorCode = "NULL_RESPONSE",
                    ErrorType = ExchangeErrorType.Unavailable
                };
            }

            if (response.RetCode != 0)
            {
                var errorType = MapBybitErrorCode(response.RetCode);
                _logger.LogWarning("BybitExecutionFailed: Order Cancel API returned non-zero code. RetCode={RetCode}, Msg={Msg}",
                    response.RetCode, response.RetMsg);

                return new OrderResult
                {
                    Success = false,
                    Status = OrderStatus.Failed,
                    ErrorMessage = response.RetMsg,
                    ErrorCode = response.RetCode.ToString(),
                    ErrorType = errorType
                };
            }

            _logger.LogInformation("BybitOrderCancelled: Order cancelled successfully. ExchangeOrderId={ExchangeOrderId}",
                exchangeOrderId);

            return new OrderResult
            {
                Success = true,
                ExchangeOrderId = response.Result?.OrderId ?? exchangeOrderId,
                Status = OrderStatus.Cancelled,
                ErrorMessage = "Order cancelled successfully."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BybitExecutionFailed: Exception during CancelOrderAsync.");
            return new OrderResult
            {
                Success = false,
                Status = OrderStatus.Failed,
                ErrorMessage = ex.Message,
                ErrorCode = "EXCEPTION",
                ErrorType = ExchangeErrorType.Unknown
            };
        }
    }

    private async Task<BybitResponse<TResult>?> SendPrivateRequestAsync<TResult>(
        HttpMethod method,
        string path,
        object? payloadOrParams,
        CancellationToken cancellationToken)
        where TResult : class
    {
        return await _resilienceService.ExecuteHttpAsync(async ct =>
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var recvWindow = _settings.RecvWindow.ToString();
            var apiKey = _settings.ApiKey;
            var apiSecret = _settings.ApiSecret;

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

            // Secure logging - NEVER log Secret, Signature, Authentication Headers, or Telegram Session
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
