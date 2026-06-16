namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationApplicationConfiguration : IEntityTypeConfiguration<CollaborationApplication>
{
    public void Configure(EntityTypeBuilder<CollaborationApplication> b)
    {
        b.ToTable("Applications");

        b.HasKey(e => e.Id);

        b.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        b.HasIndex(e => e.Name)
            .IsUnique();

        b.Property(e => e.HashedApiKey)
            .IsRequired()
            .HasMaxLength(64);   // SHA-256 hex = 64 chars

        b.HasIndex(e => e.HashedApiKey)
            .IsUnique();

        b.Property(e => e.AuthProvider)
            .IsRequired()
            .HasMaxLength(20);

        b.Property(e => e.WebhookUrl)
            .HasMaxLength(500);

        b.Property(e => e.BlobContainerPath)
            .HasMaxLength(200);

        b.Property(e => e.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        // ── Relationships ──────────────────────────────────────────────
        b.HasMany(e => e.UserTypeConfigs)
            .WithOne(c => c.Application)
            .HasForeignKey(c => c.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(e => e.ApplicationUsers)
            .WithOne(u => u.Application)
            .HasForeignKey(u => u.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
