using System.Threading;

namespace TradingBot.Parser.Templates;

public static class TemplateContext
{
    private static readonly AsyncLocal<ISignalTemplate?> _currentTemplate = new();

    public static ISignalTemplate? Current
    {
        get => _currentTemplate.Value;
        set => _currentTemplate.Value = value;
    }
}
