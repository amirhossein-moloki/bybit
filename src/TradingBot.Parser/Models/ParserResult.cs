using System.Collections.Generic;

namespace TradingBot.Parser.Models;

public class ParserResult
{
    public bool Success { get; }
    public ParsedSignal? ParsedSignal { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }
    public string ParserVersion { get; }

    private ParserResult(bool success, ParsedSignal? parsedSignal, IReadOnlyList<string> errors, IReadOnlyList<string> warnings, string parserVersion)
    {
        Success = success;
        ParsedSignal = parsedSignal;
        Errors = errors ?? System.Array.Empty<string>();
        Warnings = warnings ?? System.Array.Empty<string>();
        ParserVersion = parserVersion;
    }

    public static ParserResult SuccessResult(ParsedSignal parsedSignal, string parserVersion, IReadOnlyList<string>? warnings = null)
    {
        return new ParserResult(true, parsedSignal, System.Array.Empty<string>(), warnings ?? System.Array.Empty<string>(), parserVersion);
    }

    public static ParserResult Failure(IReadOnlyList<string> errors, string parserVersion, IReadOnlyList<string>? warnings = null)
    {
        return new ParserResult(false, null, errors, warnings ?? System.Array.Empty<string>(), parserVersion);
    }
}
