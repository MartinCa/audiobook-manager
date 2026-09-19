namespace AudiobookManager.Scraping.Models;

/// <summary>
/// An author as reported by a metadata source, returned from an author name search so the user
/// can pick which source author a library <see cref="Domain.Person"/> refers to.
/// </summary>
public class AuthorSearchResult
{
    /// <summary>
    /// The scraper's own <c>SourceName</c> (e.g. "Hardcover"). Not set by the scraper itself -
    /// mirrors <see cref="MetadataSearchResult.Source"/>, which the calling service tags results
    /// with after the scraper returns them, rather than every scraper implementation setting its
    /// own name on every result.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The source-specific identifier (e.g. a Hardcover author id).</summary>
    public string SourceId { get; set; }

    public string Name { get; set; }

    public string? SourceUrl { get; set; }

    public int? BookCount { get; set; }

    public AuthorSearchResult(string sourceId, string name)
    {
        SourceId = sourceId;
        Name = name;
    }
}
