namespace AudiobookManager.Scraping.Models;

/// <summary>
/// A series as reported by a metadata source, optionally including its full book roster.
/// Search results generally carry an empty <see cref="Books"/> list; a roster lookup fills it.
/// </summary>
public class SeriesSearchResult
{
    /// <summary>
    /// The source-specific identifier (e.g. a Hardcover series id) used to fetch the roster.
    /// </summary>
    public string SourceId { get; set; }

    public string SeriesName { get; set; }

    public string? SourceUrl { get; set; }

    public IList<string> Authors { get; set; } = new List<string>();

    public int? BookCount { get; set; }

    public IList<SeriesExpectedBookResult> Books { get; set; } = new List<SeriesExpectedBookResult>();

    public SeriesSearchResult(string sourceId, string seriesName)
    {
        SourceId = sourceId;
        SeriesName = seriesName;
    }
}

public class SeriesExpectedBookResult
{
    /// <summary>
    /// The source-specific book identifier (e.g. a Hardcover book id). Prefer over the slug-based
    /// URL as the stable identity of the roster entry - it is what the unified expected-books
    /// model dedupes on across author- and series-side refreshes.
    /// </summary>
    public string? SourceBookId { get; set; }

    public string Title { get; set; }

    public string? Position { get; set; }

    public int? Year { get; set; }

    /// <summary>A precise release date, when the source reports one - see <see cref="Database.Models.ExpectedBook.ReleaseDate"/>.</summary>
    public DateOnly? ReleaseDate { get; set; }

    public string? SourceUrl { get; set; }

    /// <summary>A cover image URL from the source, when it reports one - stored on the unified expected-book row so roster-derived upcoming items can render it.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// The authors credited on this roster entry (writing credits only - narrators are excluded),
    /// so a series refresh can attribute each book to the same people an author refresh would.
    /// </summary>
    public IList<string> Authors { get; set; } = new List<string>();

    /// <summary>
    /// Whether the source flags this roster entry as an omnibus/box-set edition rather than an
    /// individual book. Callers decide whether to keep or drop these - some libraries genuinely
    /// own the omnibus instead of the individual books it bundles.
    /// </summary>
    public bool IsCompilation { get; set; }

    public SeriesExpectedBookResult(string title)
    {
        Title = title;
    }
}
