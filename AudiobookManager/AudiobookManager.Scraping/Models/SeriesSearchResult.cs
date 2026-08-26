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
    public string Title { get; set; }

    public string? Position { get; set; }

    public int? Year { get; set; }

    public string? SourceUrl { get; set; }

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
