namespace TradingBot.Parser.Interfaces;

public interface IExtractionRule
{
    bool Match(string text);

    object Extract(string text);
}
