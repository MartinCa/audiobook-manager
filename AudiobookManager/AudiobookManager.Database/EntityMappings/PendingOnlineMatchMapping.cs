using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class PendingOnlineMatchMapping : IEntityTypeConfiguration<PendingOnlineMatch>
{
    public void Configure(EntityTypeBuilder<PendingOnlineMatch> builder)
    {
        builder
            .HasOne(m => m.Audiobook)
            .WithMany()
            .HasForeignKey(m => m.AudiobookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(m => m.AudiobookId)
            .IsUnique();
    }
}
