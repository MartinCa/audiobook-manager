namespace AudiobookManager.Scraping.Models;

/// <summary>
/// One book of an author's full bibliography, as reported by a metadata source's author lookup -
/// backs the unified expected-books roster. Unlike <see cref="UpcomingReleaseResult"/> this is not
/// filtered to not-yet-released books: the roster needs the author's whole catalog to compute both
/// missing and upcoming books.
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

    /// <summary>A cover image URL from the source, when it reports one.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>The source-specific series identifier of the book's first series, if any.</summary>
    public string? SeriesSourceId { get; set; }

    /// <summary>The series name of the book's first series, if any.</summary>
    public string? SeriesName { get; set; }

    /// <summary>The position of the book within its first series, if any.</summary>
    public string? SeriesPosition { get; set; }

    public AuthorBookResult(string sourceBookId, string title)
    {
        SourceBookId = sourceBookId;
        Title = title;
    }
}
