using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

[Table("audiobooks")]
public class Audiobook
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public List<Person> Authors { get; set; }

    public List<Person> Narrators { get; set; }

    [Required]
    [Column("book_name")]
    public string BookName { get; set; }

    [Column("subtitle")]
    public string? Subtitle { get; set; }

    [Column("series")]
    public string? Series { get; set; }

    [Column("series_part")]
    public string? SeriesPart { get; set; }

    [Required]
    [Column("year")]
    public int Year { get; set; }

    public List<Genre> Genres { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("copyright")]
    public string? Copyright { get; set; }

    [Column("publisher")]
    public string? Publisher { get; set; }

    [Column("language")]
    public string? Language { get; set; }

    [Column("rating")]
    public string? Rating { get; set; }

    [Column("asin")]
    public string? Asin { get; set; }

    [Column("www")]
    public string? Www { get; set; }

    [Column("cover_file_path")]
    public string? CoverFilePath { get; set; }

    [Column("duration_in_seconds")]
    public int? DurationInSeconds { get; set; }

    [Column("file_info_full_path")]
    public string FileInfoFullPath { get; set; }

    [Column("file_info_file_name")]
    public string FileInfoFileName { get; set; }

    [Column("file_info_size_in_bytes")]
    public long FileInfoSizeInBytes { get; set; }

    // Accent-folded shadow columns, kept in sync with BookName/Subtitle/Series/Description by
    // AccentFoldedColumnsInterceptor (a SaveChangesInterceptor, so it catches every save
    // regardless of which repository or EF cascade produced it) rather than computed by SQLite:
    // a SQLite GENERATED column would have been more self-maintaining, but STORED generated
    // columns cannot be added to a table that already has rows via ALTER TABLE ADD COLUMN (only
    // CREATE TABLE can) - which would have broken the migration against every existing library -
    // and a VIRTUAL generated column still re-runs the folding function per row for the substring
    // search this app actually does (LIKE '%x%'; only an exact-match or prefix lookup can be
    // satisfied straight from an index without recomputing, and this app never does either).
    //
    // Deliberately unindexed too: reading a plain column during a table scan is already a bare
    // access, not a callback into managed code - that's the entire fix for #1303 (the previous
    // predicates wrapped the *source* column in the fold_accents() scalar function and paid a
    // managed callback per row, per OR term, on every keystroke). An index would not change that,
    // since SQLite cannot use a B-tree index to satisfy a leading-wildcard LIKE '%x%' regardless
    // of what the column holds - "contains" isn't an index-shaped question - so one here would
    // only cost every insert/update an extra B-tree write nothing could ever use.
    [Column("book_name_folded")]
    public string? BookNameFolded { get; set; }

    [Column("subtitle_folded")]
    public string? SubtitleFolded { get; set; }

    [Column("series_folded")]
    public string? SeriesFolded { get; set; }

    [Column("description_folded")]
    public string? DescriptionFolded { get; set; }

    public Audiobook(long id, string bookName, string? subtitle, string? series, string? seriesPart, int year, string? description, string? copyright, string? publisher, string? language, string? rating, string? asin, string? www, string? coverFilePath, int? durationInSeconds, string fileInfoFullPath, string fileInfoFileName, long fileInfoSizeInBytes)
    {
        Id = id;
        BookName = bookName;
        Subtitle = subtitle;
        Series = series;
        SeriesPart = seriesPart;
        Year = year;
        Description = description;
        Copyright = copyright;
        Publisher = publisher;
        Language = language;
        Rating = rating;
        Asin = asin;
        Www = www;
        CoverFilePath = coverFilePath;
        DurationInSeconds = durationInSeconds;
        FileInfoFullPath = fileInfoFullPath;
        FileInfoFileName = fileInfoFileName;
        FileInfoSizeInBytes = fileInfoSizeInBytes;

        Authors = new List<Person>();
        Narrators = new List<Person>();
        Genres = new List<Genre>();
    }
}
