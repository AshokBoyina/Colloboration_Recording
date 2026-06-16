namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationBotMessageConfiguration : IEntityTypeConfiguration<CollaborationBotMessage>
{
    public void Configure(EntityTypeBuilder<CollaborationBotMessage> b)
    {
        b.ToTable("BotMessages");

        b.HasKey(e => e.Id);

        b.Property(e => e.Prompt)
            .HasMaxLength(2000);

        b.Property(e => e.Response)
            .IsRequired()
            .HasMaxLength(4000);

        b.Property(e => e.DetectedIntent)
            .HasMaxLength(100);

        b.Property(e => e.SentAt)
            .HasDefaultValueSql("GETUTCDATE()");

        b.HasIndex(e => e.CollaborationId);

        b.HasOne(e => e.CollaborationEntity)
            .WithMany(c => c.BotMessages)
            .HasForeignKey(e => e.CollaborationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
