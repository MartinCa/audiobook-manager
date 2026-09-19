namespace AudiobookManager.Scraping.Models;

/// <summary>
/// One book of an author's full bibliography, as reported by a metadata source's author lookup -
/// backs the author standalone-books roster (<see cref="Database.Models.AuthorExpectedBook"/>).
/// Unlike <see cref="UpcomingReleaseResult"/> this is not filtered to not-yet-released books: the
/// roster needs the author's whole catalog to compute both missing and upcoming standalone books.
/// </summary>
public class AuthorBookResult
{
    /// <summary>The source-specific book identifier (e.g. a Hardcover book id).</summary>
    public string SourceBookId { get; set; }

    public string Title { get; set; }

    public int? Year { get; set; }

    /// <summary>A precise release date, when the source reports one.</summary>
    public DateOnly? ReleaseDate { get; set; }

    public string? SourceUrl { get; set; }

    /// <summary>
    /// Whether the source lists this book under any series. A book with a series is deliberately
    /// left out of the author's standalone roster - it is already rostered (and refreshed)
    /// through that series' own <see cref="Database.Models.SeriesExpectedBook"/> roster, so
    /// including it here too would double-count it (see the design note this feature ships with).
    /// </summary>
    public bool HasSeries { get; set; }

    public AuthorBookResult(string sourceBookId, string title)
    {
        SourceBookId = sourceBookId;
        Title = title;
    }
}
