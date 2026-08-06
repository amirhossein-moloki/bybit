using System;

namespace TradingBot.Domain.Entities;

public class ParserTemplates
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public long? ChannelId { get; set; }
    public string ConfigurationJson { get; set; } = null!;
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ParserTemplates()
    {
        Id = Guid.NewGuid();
        Enabled = true;
        CreatedAt = DateTime.UtcNow;
    }
}
