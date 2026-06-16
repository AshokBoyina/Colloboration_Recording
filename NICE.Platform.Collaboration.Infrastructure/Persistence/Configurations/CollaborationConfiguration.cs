namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationConfiguration : IEntityTypeConfiguration<Collaboration>
{
    public void Configure(EntityTypeBuilder<Collaboration> b)
    {
        b.ToTable("Collaborations");

        b.HasKey(e => e.Id);

        b.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(30)
            .HasDefaultValue("Pending");

        b.Property(e => e.ChatMode)
            .IsRequired()
            .HasMaxLength(50);

        b.Property(e => e.IsScreenSharing)
            .IsRequired()
            .HasDefaultValue(false);

        b.Property(e => e.IsRecorded)
            .IsRequired()
            .HasDefaultValue(false);

        b.Property(e => e.EndReason)
            .HasMaxLength(50);

        b.Property(e => e.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        b.HasIndex(e => e.ApplicationId);
        b.HasIndex(e => e.Status);
        b.HasIndex(e => e.CreatedAt);

        b.HasOne(e => e.Application)
            .WithMany()
            .HasForeignKey(e => e.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(e => e.ExternalUser)
            .WithMany()
            .HasForeignKey(e => e.ExternalUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasMany(e => e.Participants)
            .WithOne(p => p.CollaborationEntity)
            .HasForeignKey(p => p.CollaborationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(e => e.Messages)
            .WithOne(m => m.CollaborationEntity)
            .HasForeignKey(m => m.CollaborationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(e => e.BotMessages)
            .WithOne(bm => bm.CollaborationEntity)
            .HasForeignKey(bm => bm.CollaborationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(e => e.Recordings)
            .WithOne(r => r.CollaborationEntity)
            .HasForeignKey(r => r.CollaborationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
