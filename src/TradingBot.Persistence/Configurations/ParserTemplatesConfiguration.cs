using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class ParserTemplatesConfiguration : IEntityTypeConfiguration<ParserTemplates>
{
    public void Configure(EntityTypeBuilder<ParserTemplates> builder)
    {
        builder.ToTable("ParserTemplates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.ChannelId);

        builder.Property(x => x.ConfigurationJson)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Enabled)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        // Index on ChannelId for fast matching queries
        builder.HasIndex(x => x.ChannelId);
    }
}
