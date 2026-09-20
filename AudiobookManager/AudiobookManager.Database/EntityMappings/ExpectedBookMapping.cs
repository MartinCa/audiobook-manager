using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class ExpectedBookMapping : IEntityTypeConfiguration<ExpectedBook>
{
    public void Configure(EntityTypeBuilder<ExpectedBook> builder)
    {
        builder
            .HasKey(b => b.Id)
            .HasName("pk_expected_books");

        // The dedup identity across author- and series-side refreshes. Partial because rows copied
        // from the legacy roster tables carry a synthetic source id (or no id, for an id-less
        // poll) until a refresh adopts them by natural key - see ExpectedBookRepository.UpsertAsync.
        builder
            .HasIndex(b => new { b.SourceName, b.SourceBookId }, "ix_expected_books_source")
            .IsUnique()
            .HasFilter("source_book_id IS NOT NULL");

        builder
            .HasIndex(b => b.SeriesId, "ix_expected_books_series_id");

        builder
            .HasIndex(b => b.SourceSeriesId, "ix_expected_books_source_series_id");

        builder
            .Property(b => b.IsIgnored)
            .HasDefaultValue(false);

        builder
            .Property(b => b.IsCompilation)
            .HasDefaultValue(false);

        builder
            .HasOne(b => b.Series)
            .WithMany(s => s.ExpectedBooks)
            .HasForeignKey(b => b.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasMany(b => b.AuthorLinks)
            .WithOne(l => l.ExpectedBook)
            .HasForeignKey(l => l.ExpectedBookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}