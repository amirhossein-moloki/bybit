namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IMessagePreprocessor
{
    string Preprocess(string rawContent);
}
