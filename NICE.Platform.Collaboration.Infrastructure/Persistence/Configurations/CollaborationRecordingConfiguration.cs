namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationRecordingConfiguration : IEntityTypeConfiguration<CollaborationRecording>
{
    public void Configure(EntityTypeBuilder<CollaborationRecording> b)
    {
        b.ToTable("Recordings");

        b.HasKey(e => e.Id);

        b.Property(e => e.RecordingType)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Screen");

        b.Property(e => e.BlobUri)
            .HasMaxLength(1000);

        b.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Pending");

        b.Property(e => e.StartedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        b.HasIndex(e => e.CollaborationId);
        b.HasIndex(e => e.Status);

        b.HasOne(e => e.CollaborationEntity)
            .WithMany(c => c.Recordings)
            .HasForeignKey(e => e.CollaborationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
