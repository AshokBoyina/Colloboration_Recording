namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationApplicationUserTypeConfigConfiguration
    : IEntityTypeConfiguration<CollaborationApplicationUserTypeConfig>
{
    public void Configure(EntityTypeBuilder<CollaborationApplicationUserTypeConfig> b)
    {
        b.ToTable("ApplicationUserTypeConfigs");

        b.HasKey(e => e.Id);

        b.Property(e => e.UserType)
            .IsRequired()
            .HasMaxLength(30);

        b.Property(e => e.ChatMode)
            .HasMaxLength(50);

        // One config row per UserType per application
        b.HasIndex(e => new { e.ApplicationId, e.UserType })
            .IsUnique();
    }
}
