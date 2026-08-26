using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class SeriesExpectedBookMapping : IEntityTypeConfiguration<SeriesExpectedBook>
{
    public void Configure(EntityTypeBuilder<SeriesExpectedBook> builder)
    {
        builder
            .HasKey(b => b.Id)
            .HasName("pk_series_expected_books");

        builder
            .HasIndex(b => b.SeriesId, "ix_series_expected_books_series_id");

        builder
            .Property(b => b.IsIgnored)
            .HasDefaultValue(false);
    }
}
