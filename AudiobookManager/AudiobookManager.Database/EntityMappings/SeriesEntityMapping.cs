using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class SeriesEntityMapping : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder
            .HasKey(s => s.Id)
            .HasName("pk_series");

        builder
            .HasIndex(s => s.Name, "ix_series_name")
            .IsUnique();

        // Backs ISeriesRepository.GetByMatchedSourceIdAsync - the upcoming-releases poll looks
        // up a matched series by its exact (source name, source id) pair once per author-side
        // release per sweep, which would otherwise be a full table scan.
        builder
            .HasIndex(s => new { s.MatchedSourceName, s.MatchedSourceId }, "ix_series_matched_source");

        builder
            .HasMany(s => s.ExpectedBooks)
            .WithOne(b => b.Series)
            .HasForeignKey(b => b.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
