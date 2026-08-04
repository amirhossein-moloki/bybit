using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TradingBot.Exchange.Bybit.Dtos;

public class BybitResponse<T>
{
    [JsonPropertyName("retCode")]
    public int RetCode { get; set; }

    [JsonPropertyName("retMsg")]
    public string RetMsg { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("time")]
    public long Time { get; set; }
}

public class BybitServerTime
{
    [JsonPropertyName("timeSecond")]
    public string TimeSecond { get; set; } = string.Empty;

    [JsonPropertyName("timeNano")]
    public string TimeNano { get; set; } = string.Empty;
}

public class BybitWalletBalanceResponse
{
    [JsonPropertyName("list")]
    public List<BybitAccountBalance> List { get; set; } = new();
}

public class BybitAccountBalance
{
    [JsonPropertyName("accountType")]
    public string AccountType { get; set; } = string.Empty;

    [JsonPropertyName("coin")]
    public List<BybitCoinBalance> Coin { get; set; } = new();
}

public class BybitCoinBalance
{
    [JsonPropertyName("coin")]
    public string CoinName { get; set; } = string.Empty;

    [JsonPropertyName("walletBalance")]
    public string WalletBalance { get; set; } = string.Empty;

    [JsonPropertyName("availableToWithdraw")]
    public string AvailableToWithdraw { get; set; } = string.Empty;
}

public class BybitTickerResponse
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("list")]
    public List<BybitTickerInfo> List { get; set; } = new();
}

public class BybitTickerInfo
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("lastPrice")]
    public string LastPrice { get; set; } = string.Empty;
}

public class BybitInstrumentsResponse
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("list")]
    public List<BybitInstrumentInfo> List { get; set; } = new();
}

public class BybitInstrumentInfo
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public class BybitOrderResult
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("orderLinkId")]
    public string OrderLinkId { get; set; } = string.Empty;
}

public class BybitOrderQueryResponse
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("list")]
    public List<BybitOrderInfo> List { get; set; } = new();
}

public class BybitOrderInfo
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("orderLinkId")]
    public string OrderLinkId { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public string Price { get; set; } = string.Empty;

    [JsonPropertyName("qty")]
    public string Qty { get; set; } = string.Empty;

    [JsonPropertyName("side")]
    public string Side { get; set; } = string.Empty;

    [JsonPropertyName("orderStatus")]
    public string OrderStatus { get; set; } = string.Empty;

    [JsonPropertyName("avgPrice")]
    public string AvgPrice { get; set; } = string.Empty;

    [JsonPropertyName("cumExecQty")]
    public string CumExecQty { get; set; } = string.Empty;
}
