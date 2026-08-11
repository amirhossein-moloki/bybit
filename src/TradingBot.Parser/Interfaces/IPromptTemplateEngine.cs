namespace TradingBot.Parser.Interfaces;

public interface IPromptTemplateEngine
{
    string RenderPrompt(string templateVersion, string message, string context);
}
