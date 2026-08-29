using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;
public class AudiobookMapping : IEntityTypeConfiguration<Audiobook>
{
    public void Configure(EntityTypeBuilder<Audiobook> builder)
    {
        builder
            .HasMany(a => a.Authors)
            .WithMany(p => p.BooksAuthored)
            .UsingEntity("audiobooks_authors_persons");

        builder
            .HasMany(a => a.Narrators)
            .WithMany(p => p.BooksNarrated)
            .UsingEntity("audiobooks_narrators_persons");

        builder
            .HasMany(a => a.Genres)
            .WithMany(g => g.Books);

        // The full path is the natural key every organize, scan and duplicate check looks a
        // book up by (GetByFullPathAsync, GetAllFilePathsAsync, IsDuplicateTarget), and without
        // an index each of those is a full table scan of the library. Deliberately not unique:
        // nothing in the schema guarantees two rows can't briefly share a path, and a unique
        // constraint would turn that into a hard failure mid-relocation.
        builder
            .HasIndex(a => a.FileInfoFullPath, "ix_audiobooks_file_info_full_path");

        // GetByFullPathAsync narrows on the file name before comparing paths OS-aware in
        // memory, so that column needs to be indexed for the narrowing to be worth anything.
        builder
            .HasIndex(a => a.FileInfoFileName, "ix_audiobooks_file_info_file_name");

        // Every query behind the series overview and detail views filters or groups on this
        // column: GetBooksBySeriesAsync, GetSeriesCountsByAuthorAsync, GetDistinctSeriesAsync,
        // GetSeriesGroupingDataAsync, GetSeriesNamesAsync, SearchSeriesAsync.
        builder
            .HasIndex(a => a.Series, "ix_audiobooks_series");
    }
}
