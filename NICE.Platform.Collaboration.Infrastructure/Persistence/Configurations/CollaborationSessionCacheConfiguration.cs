namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationSessionCacheConfiguration : IEntityTypeConfiguration<CollaborationSessionCache>
{
    public void Configure(EntityTypeBuilder<CollaborationSessionCache> b)
    {
        b.ToTable("SessionCache");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");

        b.Property(e => e.CacheKey)
            .IsRequired()
            .HasMaxLength(500);

        b.Property(e => e.CacheValue)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        b.Property(e => e.ExpiresAt);

        b.Property(e => e.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        // Unique: one row per key
        b.HasIndex(e => e.CacheKey)
            .IsUnique();

        // Index for expiry sweeps (background cleanup selects WHERE ExpiresAt < GETUTCDATE())
        b.HasIndex(e => e.ExpiresAt);
    }
}
