using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// One expected book discovered from a metadata source - the unified model that replaced the two
/// parallel roster tables (<c>author_expected_books</c> per author, <c>series_expected_books</c>
/// per series; both dropped once this model and the copy migration landed).
/// A book is linked to its author(s) through <see cref="ExpectedBookAuthor"/> rows and optionally
/// to a <see cref="Series"/>, so the same source book discovered by both an author refresh and a
/// series refresh is one row instead of two. <see cref="SourceBookId"/> is the dedup identity:
/// rows copied from the legacy roster tables carry a synthetic id with the
/// <see cref="LegacyAuthorSyntheticPrefix"/>/<see cref="LegacySeriesSyntheticPrefix"/> prefix
/// until a refresh adopts them by natural key and overwrites it with the real source id (see
/// <c>ExpectedBookRepository.UpsertAsync</c>).
/// </summary>
[Table("expected_books")]
public class ExpectedBook
{
    /// <summary>Prefix of the synthetic <see cref="SourceBookId"/> carried by rows copied from the legacy <c>author_expected_books</c> table.</summary>
    public const string LegacyAuthorSyntheticPrefix = "legacy-author:";

    /// <summary>Prefix of the synthetic <see cref="SourceBookId"/> carried by rows copied from the legacy <c>series_expected_books</c> table.</summary>
    public const string LegacySeriesSyntheticPrefix = "legacy-series:";

    /// <summary>
    /// Whether <paramref name="sourceBookId"/> is one of the synthetic ids the legacy-roster copy
    /// wrote. The <c>legacy-</c> namespace is reserved: a metadata source must never mint a book
    /// id that starts with it, or the id would be indistinguishable from a fabricated placeholder
    /// and adoption could pair it with the wrong row.
    /// </summary>
    public static bool IsLegacySyntheticSourceBookId(string? sourceBookId) =>
        sourceBookId is not null &&
        (sourceBookId.StartsWith(LegacyAuthorSyntheticPrefix, StringComparison.Ordinal) ||
            sourceBookId.StartsWith(LegacySeriesSyntheticPrefix, StringComparison.Ordinal));
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>The metadata source that reported this book (e.g. "Hardcover").</summary>
    [Required]
    [Column("source_name")]
    public string SourceName { get; set; } = string.Empty;

    /// <summary>
    /// The source-specific book identifier - the dedup identity. A row copied from the legacy
    /// roster tables carries a synthetic <see cref="LegacyAuthorSyntheticPrefix"/>/
    /// <see cref="LegacySeriesSyntheticPrefix"/> id until a refresh adopts it by natural key
    /// (see <c>ExpectedBookRepository.UpsertAsync</c>); a poll that lacks the source's id at all
    /// stores null.
    /// </summary>
    [Column("source_book_id")]
    public string? SourceBookId { get; set; }

    [Required]
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("year")]
    public int? Year { get; set; }

    /// <summary>A precise release date, when the source reports one - preferred over <see cref="Year"/> for Missing-vs-Upcoming classification.</summary>
    [Column("release_date")]
    public DateOnly? ReleaseDate { get; set; }

    [Column("source_url")]
    public string? SourceUrl { get; set; }

    [Column("image_url")]
    public string? ImageUrl { get; set; }

    /// <summary>
    /// The catalog series this book belongs to, when a matched series is known. Null for a
    /// standalone book, but also cleared by <c>ExpectedBookRepository.UnlinkSeriesBooksAsync</c>
    /// when a series no longer reports this book - the row (and its author links) survives.
    /// Set to null (not cascading) when the series row itself is deleted.
    /// </summary>
    [Column("series_id")]
    public long? SeriesId { get; set; }

    public Series? Series { get; set; }

    /// <summary>The source-specific series identifier, when the source places this book in a series.</summary>
    [Column("source_series_id")]
    public string? SourceSeriesId { get; set; }

    /// <summary>The series name as the source reports it, when the source places this book in a series.</summary>
    [Column("source_series_name")]
    public string? SourceSeriesName { get; set; }

    /// <summary>Position within the series, kept as a string to match <see cref="Audiobook.SeriesPart"/> (which allows values like "1.5").</summary>
    [Column("series_position")]
    public string? SeriesPosition { get; set; }

    /// <summary>
    /// Whether the source flags this book as an omnibus/box-set edition rather than an individual
    /// book. Only meaningful for series-rostered books.
    /// </summary>
    [Column("is_compilation")]
    public bool IsCompilation { get; set; }

    /// <summary>Lets a user dismiss a book they never intend to own; it stays stored, just hidden from Missing/Upcoming.</summary>
    [Column("is_ignored")]
    public bool IsIgnored { get; set; }

    /// <summary>When this book was first seen by any refresh.</summary>
    [Column("first_seen_at")]
    public DateTime FirstSeenAt { get; set; }

    /// <summary>When the source data for this book was last refreshed.</summary>
    [Column("last_refreshed_at")]
    public DateTime LastRefreshedAt { get; set; }

    public List<ExpectedBookAuthor> AuthorLinks { get; set; } = new();
}