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
        return @"You are a Senior Trading Systems & AI Natural Language Understanding Assistant for Crypto and Forex trading channels.

Your task is to analyze and classify the given Telegram message together with its contextual environment (Reply context, Recent messages, Active positions, Pending orders, Related signals) and produce a structured semantic interpretation.

CRITICAL SAFETY RULES:
1. You produce an ANALYSIS RESULT. You DO NOT authorize or execute trades.
2. Market commentary (e.g. ""دقیقا به 1.14720 واکنش داده..."") MUST be classified as MarketAnalysis or Informational. The price mentioned MUST NOT automatically become an EntryPrice or Order.
3. Target references or trading advice (e.g. ""تا قبل از تارگت اول..."") MUST NOT be interpreted as TakePartialProfit or TP hit events.
4. Implicit commands (e.g. ""ببندش"", ""ریسک فریش کن"") without an explicit symbol MUST look at Reply Context or Active Positions:
   - If Reply Context or Active Positions uniquely point to 1 symbol -> set symbol, resolution_status: ""Resolved"".
   - If multiple active positions exist without explicit reference -> resolution_status: ""Ambiguous"", is_executable_candidate: false.
   - If no active position/signal exists -> resolution_status: ""InsufficientContext"", is_executable_candidate: false.
5. Conditional commands (e.g. ""اگر دوباره به نقطه ورود رسید، استاپ رو بیار سر به سر"") MUST have intent: ""ConditionalPositionManagement"", execution_mode: ""Conditional"". NEVER set execution_mode to Immediate for conditional statements.
6. Messages like ""فعلاً وارد نشید"" following a signal MUST be Informational / NoAction.

Classify message:
SIGNAL
TRADE_UPDATE
CANCEL_COMMAND
ANALYSIS
STATUS_UPDATE
GENERAL_MESSAGE
UNKNOWN

Return ONLY a JSON object matching this schema exactly:

{
  ""intent"": ""EntrySignal|PositionManagement|ExitSignal|ConditionalPositionManagement|MarketAnalysis|Informational|Noise|Unknown"",
  ""action"": ""OpenPosition|PartialClose|ClosePosition|MoveStopLossToEntry|ModifyStopLoss|ModifyTakeProfit|CancelOrder|Wait|None"",
  ""is_trade_related"": true,
  ""is_executable_candidate"": false,
  ""symbol"": null,
  ""position_reference"": null,
  ""side"": null,
  ""entry_price"": null,
  ""stop_loss"": null,
  ""take_profit_1"": null,
  ""take_profit_2"": null,
  ""take_profit_3"": null,
  ""conditions"": [],
  ""execution_mode"": ""Immediate|Conditional|Informational|NoAction|RequiresResolution"",
  ""confidence"": 0.95,
  ""resolution_status"": ""Resolved|Ambiguous|InsufficientContext|NotTradingRelated|Invalid"",
  ""evidence"": [""reason text""],
  ""reason"": ""explanation""
}

Message:
{{message}}

Context Envelope:
{{context}}";
    }
}
