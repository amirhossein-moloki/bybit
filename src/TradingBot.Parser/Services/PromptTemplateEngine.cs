using System;
using System.IO;
using TradingBot.Application.Monitoring;
using TradingBot.Parser.Interfaces;

namespace TradingBot.Parser.Services;

public class PromptTemplateEngine : IPromptTemplateEngine
{
    private readonly IEventSanitizer _sanitizer;

    public PromptTemplateEngine(IEventSanitizer sanitizer)
    {
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
    }

    public string RenderPrompt(string templateVersion, string message, string context)
    {
        var sanitizedMessage = _sanitizer.Sanitize(message) ?? string.Empty;
        var sanitizedContext = _sanitizer.Sanitize(context) ?? string.Empty;

        string template = GetDefaultTemplate();
        try
        {
            var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates", "AIPrompts", $"prompt_{templateVersion}.txt");
            if (File.Exists(templatePath))
            {
                template = File.ReadAllText(templatePath);
            }
        }
        catch
        {
            // Fail-safe
        }

        return template
            .Replace("{{message}}", sanitizedMessage)
            .Replace("{{context}}", sanitizedContext);
    }

    private string GetDefaultTemplate()
    {
        return @"You are a trading message understanding assistant.

Your ONLY task is message classification.

You MUST NOT:
- create trades
- give financial advice
- calculate risk
- decide execution

Classify message:

SIGNAL
TRADE_UPDATE
CANCEL_COMMAND
ANALYSIS
STATUS_UPDATE
GENERAL_MESSAGE
UNKNOWN


Return ONLY JSON.

Schema:

{
""type"":"""",
""action"":"""",
""symbol"":"""",
""side"":"""",
""entry"":null,
""stop_loss"":null,
""take_profit"":[],
""confidence"":0,
""reason"":""""
}


Message:
{{message}}


Context:
{{context}}";
    }
}
