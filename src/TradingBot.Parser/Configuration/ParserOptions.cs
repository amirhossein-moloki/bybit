namespace TradingBot.Parser.Configuration;

public class ParserOptions
{
    public const string SectionName = "Parser";

    public string Version { get; set; } = "1.0";
    public int MaxMessageLength { get; set; } = 5000;
}
