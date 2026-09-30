using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class AudiobookSeriesMapping : IEntityTypeConfiguration<AudiobookSeries>
{
    public void Configure(EntityTypeBuilder<AudiobookSeries> builder)
    {
        // A book relates to a series once. Ordinal (BINARY) like every other name column; the
        // service dedupes case-insensitively before it ever gets here.
        builder
            .HasIndex(r => new { r.AudiobookId, r.SeriesName }, "ux_audiobook_series_book_series")
            .IsUnique();

        // At most one primary per book - the mirror onto audiobooks.series has nowhere to point
        // if there were two.
        builder
            .HasIndex(r => r.AudiobookId, "ux_audiobook_series_primary")
            .IsUnique()
            .HasFilter("is_primary = 1");

        // Every series-page query looks relations up by series name.
        builder
            .HasIndex(r => r.SeriesName, "ix_audiobook_series_series_name");
    }
}
