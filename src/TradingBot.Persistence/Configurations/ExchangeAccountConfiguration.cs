using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class ExchangeAccountConfiguration : IEntityTypeConfiguration<ExchangeAccount>
{
    public void Configure(EntityTypeBuilder<ExchangeAccount> builder)
    {
        builder.ToTable("ExchangeAccounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExchangeName)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Environment)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.EncryptedApiKey)
            .HasColumnName("ApiKeyEncrypted")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.EncryptedSecret)
            .HasColumnName("SecretEncrypted")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();
    }
}
