namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationParticipantConfiguration : IEntityTypeConfiguration<CollaborationParticipant>
{
    public void Configure(EntityTypeBuilder<CollaborationParticipant> b)
    {
        b.ToTable("Participants");

        b.HasKey(e => e.Id);

        b.Property(e => e.UserType)
            .IsRequired()
            .HasMaxLength(30);

        b.Property(e => e.JoinedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        b.HasIndex(e => e.CollaborationId);
        b.HasIndex(e => new { e.CollaborationId, e.UserId });

        b.HasOne(e => e.CollaborationEntity)
            .WithMany(c => c.Participants)
            .HasForeignKey(e => e.CollaborationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
