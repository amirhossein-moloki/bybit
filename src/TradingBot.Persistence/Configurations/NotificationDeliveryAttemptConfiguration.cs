using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class NotificationDeliveryAttemptConfiguration : IEntityTypeConfiguration<NotificationDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryAttempt> builder)
    {
        builder.ToTable("NotificationDeliveryAttempts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.NotificationId)
            .IsRequired();

        builder.Property(x => x.AttemptNumber)
            .IsRequired();

        builder.Property(x => x.AttemptedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.IsSuccess)
            .IsRequired();

        builder.Property(x => x.ErrorCode)
            .HasMaxLength(100);

        builder.Property(x => x.ErrorMessage)
            .HasColumnType("text");

        // Indexes
        builder.HasIndex(x => x.NotificationId);
        builder.HasIndex(x => x.AttemptedAt);
    }
}
