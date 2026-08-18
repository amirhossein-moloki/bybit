namespace TradingBot.Infrastructure.Configuration;

public class TradingBotSettings
{
    public ApplicationSettings Application { get; set; } = new();
    public DatabaseSettings Database { get; set; } = new();
    public ExchangeSettings Exchange { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public ExecutionSettings Execution { get; set; } = new();
}

public class ApplicationSettings
{
    public string Environment { get; set; } = "Development";
    public string BotName { get; set; } = "TelegramSignalTradingBot";
}

public class DatabaseSettings
{
    public string ConnectionString { get; set; } = string.Empty;
}

public class ExchangeSettings
{
    public string SelectedExchange { get; set; } = "Bybit";
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool UseSandbox { get; set; } = true;
    public string Environment { get; set; } = "Testnet";
    public int RecvWindow { get; set; } = 5000;
    public string ProxyUrl { get; set; } = string.Empty;
}

public class LoggingSettings
{
    public string LogLevel { get; set; } = "Information";
    public bool EnableConsole { get; set; } = true;
    public string LogFilePath { get; set; } = "logs/tradingbot.log";
}

public class SecuritySettings
{
    public string EncryptionKey { get; set; } = string.Empty;
    public string AllowedTelegramChatIds { get; set; } = string.Empty;
}
