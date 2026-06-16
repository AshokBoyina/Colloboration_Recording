namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationWebhookNotificationConfiguration
    : IEntityTypeConfiguration<CollaborationWebhookNotification>
{
    public void Configure(EntityTypeBuilder<CollaborationWebhookNotification> b)
    {
        b.ToTable("WebhookNotifications");

        b.HasKey(e => e.Id);

        b.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(100);

        // Payload stored as nvarchar(max)
        b.Property(e => e.Payload)
            .IsRequired();

        b.Property(e => e.WebhookUrl)
            .IsRequired()
            .HasMaxLength(500);

        b.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Pending");

        b.Property(e => e.LastError)
            .HasMaxLength(1000);

        b.Property(e => e.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        b.HasIndex(e => e.ApplicationId);
        b.HasIndex(e => e.Status);
        b.HasIndex(e => e.CreatedAt);

        b.HasOne(e => e.Application)
            .WithMany()
            .HasForeignKey(e => e.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
