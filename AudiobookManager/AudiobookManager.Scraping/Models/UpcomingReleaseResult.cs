namespace AudiobookManager.Scraping.Models;

/// <summary>
/// One not-yet-released book reported by a metadata source for a followed author or series,
/// as of the poll that discovered it.
/// </summary>
public class UpcomingReleaseResult
{
    /// <summary>The source-specific book identifier (e.g. a Hardcover book id).</summary>
    public string SourceBookId { get; set; }

    public string Title { get; set; }

    public DateOnly ReleaseDate { get; set; }

    public string? SourceUrl { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>The series this book belongs to on the source, if any - name only.</summary>
    public string? SeriesName { get; set; }

    /// <summary>The source-specific series identifier, if this book belongs to one.</summary>
    public string? SeriesSourceId { get; set; }

    public string? SeriesPosition { get; set; }

    public UpcomingReleaseResult(string sourceBookId, string title, DateOnly releaseDate)
    {
        SourceBookId = sourceBookId;
        Title = title;
        ReleaseDate = releaseDate;
    }
}
