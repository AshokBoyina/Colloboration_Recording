namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationAuditLogConfiguration : IEntityTypeConfiguration<CollaborationAuditLog>
{
    public void Configure(EntityTypeBuilder<CollaborationAuditLog> b)
    {
        b.ToTable("AuditLogs");

        b.HasKey(e => e.Id);

        b.Property(e => e.Category)
            .IsRequired()
            .HasMaxLength(30);

        b.Property(e => e.EventName)
            .IsRequired()
            .HasMaxLength(100);

        // JSON payload — no explicit max so EF maps to nvarchar(max)
        b.Property(e => e.Payload);

        b.Property(e => e.IpAddress)
            .HasMaxLength(45);   // IPv6 max = 45 chars

        b.Property(e => e.OccurredAt)
            .HasDefaultValueSql("GETUTCDATE()");

        // AuditLog is append-only: no FK constraints with cascade delete
        b.HasIndex(e => e.OccurredAt);
        b.HasIndex(e => e.EventName);
        b.HasIndex(e => new { e.CollaborationId, e.OccurredAt });
        b.HasIndex(e => new { e.ApplicationId, e.OccurredAt });
    }
}
