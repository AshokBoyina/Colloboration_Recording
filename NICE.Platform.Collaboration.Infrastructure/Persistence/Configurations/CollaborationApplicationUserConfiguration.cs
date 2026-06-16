namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationApplicationUserConfiguration : IEntityTypeConfiguration<CollaborationApplicationUser>
{
    public void Configure(EntityTypeBuilder<CollaborationApplicationUser> b)
    {
        b.ToTable("ApplicationUsers");

        b.HasKey(e => new { e.ApplicationId, e.UserId });

        b.Property(e => e.Role)
            .IsRequired()
            .HasMaxLength(30);

        b.Property(e => e.AddedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        b.HasOne(e => e.Application)
            .WithMany(a => a.ApplicationUsers)
            .HasForeignKey(e => e.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(e => e.User)
            .WithMany(u => u.ApplicationUsers)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
