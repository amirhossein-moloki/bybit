using System;
using System.Collections.Generic;
using System.Linq;
using TradingBot.Domain.Entities;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Templates;

public class SignalTemplate : ISignalTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = null!;
    public long? ChannelId { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public List<TemplateRule> Rules { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual bool CanHandle(ParserContext context)
    {
        if (context == null) return false;
        if (!Enabled) return false;

        // Channel ID Check
        if (ChannelId.HasValue)
        {
            if (long.TryParse(context.SourceChannel, out var sourceChannelLong))
            {
                if (sourceChannelLong != ChannelId.Value)
                {
                    return false;
                }
            }
            else if (context.SourceChannel != ChannelId.Value.ToString())
            {
                return false;
            }
        }

        // Pattern Matching: For generic templates (ChannelId == null), check if any template rule pattern is contained in the message.
        if (!ChannelId.HasValue && Rules.Count > 0)
        {
            var normalized = SignalTextNormalizer.Normalize(context.RawMessage);
            return Rules.Any(r => !string.IsNullOrEmpty(r.Pattern) && normalized.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase));
        }

        return true;
    }

    public IReadOnlyList<TemplateRule> GetRules()
    {
        return Rules;
    }

    public static SignalTemplate FromEntity(ParserTemplates entity)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        var rules = new List<TemplateRule>();
        if (!string.IsNullOrWhiteSpace(entity.ConfigurationJson))
        {
            try
            {
                rules = System.Text.Json.JsonSerializer.Deserialize<List<TemplateRule>>(entity.ConfigurationJson) ?? new List<TemplateRule>();
            }
            catch
            {
                // Return empty rules, fallback logging or handling is done by the caller
            }
        }

        return new SignalTemplate
        {
            Id = entity.Id,
            Name = entity.Name,
            ChannelId = entity.ChannelId,
            Enabled = entity.Enabled,
            Rules = rules,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
