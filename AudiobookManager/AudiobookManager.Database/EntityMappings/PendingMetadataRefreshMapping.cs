using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class PendingMetadataRefreshMapping : IEntityTypeConfiguration<PendingMetadataRefresh>
{
    public void Configure(EntityTypeBuilder<PendingMetadataRefresh> builder)
    {
        builder
            .HasIndex(p => p.AudiobookId)
            .IsUnique();

        builder
            .HasOne(p => p.Audiobook)
            .WithMany()
            .HasForeignKey(p => p.AudiobookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}