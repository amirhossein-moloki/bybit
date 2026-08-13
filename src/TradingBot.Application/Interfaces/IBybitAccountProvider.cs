using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Application.Interfaces;

public class BybitAccountInfo
{
    public string Name { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "Testnet"; // "Production", "Testnet", "Demo"
}

public class BybitAccountSettings
{
    public string Name { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "Testnet"; // "Production", "Testnet", "Demo"
    public bool IsActive { get; set; } = true;
}

public interface IBybitAccountProvider
{
    Task<List<BybitAccountInfo>> GetActiveAccountsAsync(CancellationToken cancellationToken = default);
}

public class SingleBybitAccountProvider : IBybitAccountProvider
{
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _environment;

    public SingleBybitAccountProvider(string apiKey, string apiSecret, string environment)
    {
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _environment = environment;
    }

    public Task<List<BybitAccountInfo>> GetActiveAccountsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<BybitAccountInfo>
        {
            new BybitAccountInfo
            {
                Name = "Default",
                ApiKey = _apiKey,
                ApiSecret = _apiSecret,
                Environment = _environment
            }
        });
    }
}
