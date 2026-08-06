namespace TradingBot.Parser.Configuration;

public class ParserTemplatesOptions
{
    public const string SectionName = "ParserTemplates";

    public bool EnableDatabaseTemplates { get; set; } = true;
    public string FallbackTemplate { get; set; } = "Default";
}
