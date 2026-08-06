using System;
using System.Collections.Generic;
using System.Linq;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Templates;

public static class TemplateMatcher
{
    public static ISignalTemplate? Match(IEnumerable<ISignalTemplate> templates, ParserContext context)
    {
        if (templates == null || context == null) return null;

        // Channel ID Check & Template Enabled Check (handled in CanHandle) & Pattern Matching (handled in CanHandle for generic templates)
        // Find channel-specific matches:
        var channelMatches = templates
            .Where(t => t is SignalTemplate st && st.Enabled && st.ChannelId.HasValue && st.CanHandle(context))
            .Cast<SignalTemplate>()
            .OrderByDescending(st => st.Priority)
            .ThenByDescending(st => st.CreatedAt)
            .ToList();

        if (channelMatches.Any())
        {
            return channelMatches.First();
        }

        // Generic templates:
        var genericMatches = templates
            .Where(t => t is SignalTemplate st && st.Enabled && !st.ChannelId.HasValue && st.CanHandle(context))
            .Cast<SignalTemplate>()
            .OrderByDescending(st => st.Priority)
            .ThenByDescending(st => st.CreatedAt)
            .ToList();

        if (genericMatches.Any())
        {
            return genericMatches.First();
        }

        return null;
    }
}
