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

        // At most one primary per book - it is the book's series, so there cannot be two.
        builder
            .HasIndex(r => r.AudiobookId, "ux_audiobook_series_primary")
            .IsUnique()
            .HasFilter("is_primary = 1");

        // The book search matches a book's primary series name with a LIKE '%x%' scan. This narrow,
        // covering index (name + book id, primaries only) is what that scan reads instead of the
        // whole table.
        builder
            .HasIndex(r => new { r.SeriesNameFolded, r.AudiobookId }, "ix_audiobook_series_primary_folded")
            .HasFilter("is_primary = 1");

        // Every series-page query looks relations up by series name.
        builder
            .HasIndex(r => r.SeriesName, "ix_audiobook_series_series_name");
    }
}
