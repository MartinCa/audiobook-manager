using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class SeriesFollowMapping : IEntityTypeConfiguration<SeriesFollow>
{
    public void Configure(EntityTypeBuilder<SeriesFollow> builder)
    {
        builder
            .HasKey(f => f.Id)
            .HasName("pk_series_follows");

        builder
            .HasIndex(f => f.SeriesId, "ix_series_follows_series_id")
            .IsUnique();

        builder
            .HasOne(f => f.Series)
            .WithMany()
            .HasForeignKey(f => f.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
