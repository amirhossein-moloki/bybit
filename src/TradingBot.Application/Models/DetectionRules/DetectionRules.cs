namespace TradingBot.Application.Models;

public class DetectionRules
{
    public EnglishRules English { get; set; } = new();
    public PersianRules Persian { get; set; } = new();
    public CustomRules Custom { get; set; } = new();
}
