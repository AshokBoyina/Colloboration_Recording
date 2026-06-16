namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationTransferRequestConfiguration : IEntityTypeConfiguration<CollaborationTransferRequest>
{
    public void Configure(EntityTypeBuilder<CollaborationTransferRequest> b)
    {
        b.ToTable("TransferRequests");

        b.HasKey(e => e.Id);

        b.Property(e => e.ToQueue)
            .HasMaxLength(100);

        b.Property(e => e.TransferNote)
            .HasMaxLength(500);

        b.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Pending");

        b.Property(e => e.RequestedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        b.HasIndex(e => e.CollaborationId);
        b.HasIndex(e => e.Status);

        b.HasOne(e => e.CollaborationEntity)
            .WithMany(c => c.Transfers)
            .HasForeignKey(e => e.CollaborationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(e => e.FromUser)
            .WithMany()
            .HasForeignKey(e => e.FromUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(e => e.ToUser)
            .WithMany()
            .HasForeignKey(e => e.ToUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
