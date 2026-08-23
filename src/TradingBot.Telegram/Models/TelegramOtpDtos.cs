using System.Text.Json.Serialization;

namespace TradingBot.Telegram.Models;

public class OtpStartResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("phoneCodeHash")]
    public string? PhoneCodeHash { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class OtpVerifyResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("requiresPassword")]
    public bool RequiresPassword { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class PasswordResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class OtpStartRequest
{
    [JsonPropertyName("phoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class OtpVerifyRequest
{
    [JsonPropertyName("phoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("phoneCodeHash")]
    public string PhoneCodeHash { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
}

public class PasswordVerifyRequest
{
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}
