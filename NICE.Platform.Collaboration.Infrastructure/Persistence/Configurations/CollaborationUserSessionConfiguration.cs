namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationUserSessionConfiguration : IEntityTypeConfiguration<CollaborationUserSession>
{
    public void Configure(EntityTypeBuilder<CollaborationUserSession> b)
    {
        b.ToTable("UserSessions");

        b.HasKey(e => e.Id);

        b.Property(e => e.UserType)
            .IsRequired()
            .HasMaxLength(30);

        b.Property(e => e.AuthProvider)
            .IsRequired()
            .HasMaxLength(20);

        b.Property(e => e.EndReason)
            .HasMaxLength(50);

        b.Property(e => e.ConnectedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        b.HasIndex(e => e.UserId);
        b.HasIndex(e => e.ApplicationId);
        b.HasIndex(e => e.ConnectedAt);

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
