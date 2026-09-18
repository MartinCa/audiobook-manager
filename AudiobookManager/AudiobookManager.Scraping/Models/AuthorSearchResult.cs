namespace AudiobookManager.Scraping.Models;

/// <summary>
/// An author as reported by a metadata source, returned from an author name search so the user
/// can pick which source author a library <see cref="Domain.Person"/> refers to.
/// </summary>
public class AuthorSearchResult
{
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
