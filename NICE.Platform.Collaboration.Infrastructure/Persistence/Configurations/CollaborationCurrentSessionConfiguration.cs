namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationCurrentSessionConfiguration : IEntityTypeConfiguration<CollaborationCurrentSession>
{
    public void Configure(EntityTypeBuilder<CollaborationCurrentSession> b)
    {
        b.ToTable("CurrentSessions");

        b.HasKey(e => e.Id);

        b.Property(e => e.UserType)
            .IsRequired()
            .HasMaxLength(30);

        b.Property(e => e.AuthProvider)
            .IsRequired()
            .HasMaxLength(20);

        b.Property(e => e.SignalRConnectionId)
            .HasMaxLength(256);

        b.Property(e => e.ConnectedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        b.Property(e => e.LastSeenAt)
            .HasDefaultValueSql("GETUTCDATE()");

        // Index for fast "find sessions by application" queries
        b.HasIndex(e => e.ApplicationId);

        // Index for real-time presence lookups (who is online per app+role)
        b.HasIndex(e => new { e.ApplicationId, e.UserType });

        b.HasOne(e => e.Application)
            .WithMany()
            .HasForeignKey(e => e.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
