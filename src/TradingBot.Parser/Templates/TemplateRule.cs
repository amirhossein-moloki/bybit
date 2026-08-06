namespace TradingBot.Parser.Templates;

public class TemplateRule
{
    public string Field { get; set; } = null!;
    public string Pattern { get; set; } = null!;
    public string Extractor { get; set; } = null!;
    public bool Required { get; set; }
    public int Order { get; set; }
}
