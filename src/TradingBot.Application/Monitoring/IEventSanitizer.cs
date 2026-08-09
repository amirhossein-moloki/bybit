namespace TradingBot.Application.Monitoring;

public interface IEventSanitizer
{
    string? Sanitize(string? input);
    string? SanitizeAndLimit(string? input, int maxLength);
}
