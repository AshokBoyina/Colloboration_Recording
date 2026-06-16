namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationAttachmentConfiguration : IEntityTypeConfiguration<CollaborationAttachment>
{
    public void Configure(EntityTypeBuilder<CollaborationAttachment> b)
    {
        b.ToTable("Attachments");

        b.HasKey(e => e.Id);

        b.Property(e => e.FileName)
            .IsRequired()
            .HasMaxLength(260);

        b.Property(e => e.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        b.Property(e => e.BlobUri)
            .IsRequired()
            .HasMaxLength(1000);

        b.Property(e => e.ThumbnailUri)
            .HasMaxLength(1000);

        b.Property(e => e.UploadedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        b.HasOne(e => e.Message)
            .WithMany(m => m.Attachments)
            .HasForeignKey(e => e.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
