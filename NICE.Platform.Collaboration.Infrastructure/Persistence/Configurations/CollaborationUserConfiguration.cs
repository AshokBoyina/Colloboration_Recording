namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationUserConfiguration : IEntityTypeConfiguration<CollaborationUser>
{
    public void Configure(EntityTypeBuilder<CollaborationUser> b)
    {
        b.ToTable("Users");

        b.HasKey(e => e.Id);

        b.Property(e => e.ExternalUserId)
            .IsRequired()
            .HasMaxLength(450);   // 450 = max indexable nvarchar on SQL Server; covers JWTs & opaque IDs

        b.HasIndex(e => e.ExternalUserId)
            .IsUnique();

        b.Property(e => e.FirstName).HasMaxLength(100);
        b.Property(e => e.LastName).HasMaxLength(100);

        b.Property(e => e.Email)
            .IsRequired()
            .HasMaxLength(256);

        b.HasIndex(e => e.Email);

        b.Property(e => e.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");
    }
}
