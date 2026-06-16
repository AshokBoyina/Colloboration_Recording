namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationMessageConfiguration : IEntityTypeConfiguration<CollaborationMessage>
{
    public void Configure(EntityTypeBuilder<CollaborationMessage> b)
    {
        b.ToTable("Messages");

        b.HasKey(e => e.Id);

        b.Property(e => e.SenderType)
            .IsRequired()
            .HasMaxLength(30);

        b.Property(e => e.Body)
            .HasMaxLength(4000);

        b.Property(e => e.MessageType)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Text");

        b.Property(e => e.SentAt)
            .HasDefaultValueSql("GETUTCDATE()");

        b.HasIndex(e => e.CollaborationId);
        b.HasIndex(e => e.SentAt);

        b.HasOne(e => e.CollaborationEntity)
            .WithMany(c => c.Messages)
            .HasForeignKey(e => e.CollaborationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(e => e.Sender)
            .WithMany()
            .HasForeignKey(e => e.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(e => e.Attachments)
            .WithOne(a => a.Message)
            .HasForeignKey(a => a.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
