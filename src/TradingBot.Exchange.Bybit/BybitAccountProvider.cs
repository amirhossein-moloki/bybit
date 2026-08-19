using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Repositories;

namespace TradingBot.Exchange.Bybit;

public class BybitAccountProvider : IBybitAccountProvider
{
    private readonly BybitSettings _settings;
    private readonly IExchangeAccountRepository _accountRepository;
    private readonly IEncryptionService _encryptionService;

    public BybitAccountProvider(
        BybitSettings settings,
        IExchangeAccountRepository accountRepository,
        IEncryptionService encryptionService)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
    }

    public async Task<List<BybitAccountInfo>> GetActiveAccountsAsync(CancellationToken cancellationToken = default)
    {
        var accounts = new List<BybitAccountInfo>();

        // 1. Add default account if configured in root BybitSettings
        var defaultApiKey = _settings.EffectiveApiKey;
        var defaultApiSecret = _settings.EffectiveApiSecret;
        if (!string.IsNullOrEmpty(defaultApiKey) && !string.IsNullOrEmpty(defaultApiSecret))
        {
            accounts.Add(new BybitAccountInfo
            {
                Name = "Default",
                ApiKey = defaultApiKey,
                ApiSecret = defaultApiSecret,
                Environment = _settings.Environment
            });
        }

        // 2. Add accounts from the configured Accounts list in BybitSettings (appsettings.json)
        if (_settings.Accounts != null)
        {
            foreach (var configAcc in _settings.Accounts)
            {
                if (configAcc.IsActive && !string.IsNullOrEmpty(configAcc.ApiKey) && !string.IsNullOrEmpty(configAcc.ApiSecret))
                {
                    // Check if already added to avoid exact duplicates
                    if (!accounts.Any(a => a.ApiKey == configAcc.ApiKey))
                    {
                        accounts.Add(new BybitAccountInfo
                        {
                            Name = configAcc.Name,
                            ApiKey = configAcc.ApiKey,
                            ApiSecret = configAcc.ApiSecret,
                            Environment = configAcc.Environment
                        });
                    }
                }
            }
        }

        // 3. Add accounts from the database
        try
        {
            var dbAccounts = await _accountRepository.GetAllAsync(cancellationToken);
            if (dbAccounts != null)
            {
                foreach (var dbAcc in dbAccounts)
                {
                    if (dbAcc.Status == Domain.Enums.ExchangeAccountStatus.Active &&
                        string.Equals(dbAcc.ExchangeName, "BYBIT", System.StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var apiKey = _encryptionService.Decrypt(dbAcc.EncryptedApiKey);
                            var apiSecret = _encryptionService.Decrypt(dbAcc.EncryptedSecret);

                            if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
                            {
                                if (!accounts.Any(a => a.ApiKey == apiKey))
                                {
                                    accounts.Add(new BybitAccountInfo
                                    {
                                        Name = $"DBAccount-{dbAcc.Id}",
                                        ApiKey = apiKey,
                                        ApiSecret = apiSecret,
                                        Environment = dbAcc.Environment
                                    });
                                }
                            }
                        }
                        catch (Exception decryptEx)
                        {
                            // Log and handle decryption failures gracefully
                            Console.WriteLine($"[BybitAccountProvider] Decryption failed for exchange account {dbAcc.Id}: {decryptEx.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception dbEx)
        {
            // Database may not be initialized yet during startup recovery ping/tests, fail gracefully
            Console.WriteLine($"[BybitAccountProvider] Failed to load accounts from database: {dbEx.Message}");
        }

        // 4. Ensure we always have at least one account to work with
        if (!accounts.Any())
        {
            accounts.Add(new BybitAccountInfo
            {
                Name = "Fallback",
                ApiKey = defaultApiKey,
                ApiSecret = defaultApiSecret,
                Environment = _settings.Environment
            });
        }

        return accounts;
    }
}
