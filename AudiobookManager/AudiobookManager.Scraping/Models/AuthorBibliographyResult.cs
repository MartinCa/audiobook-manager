namespace AudiobookManager.Scraping.Models;

/// <summary>
/// An author's full bibliography as fetched from a source, together with the name the source
/// itself uses for the author. The name is what lets a roster refresh notice that the source
/// spells the author differently from the library (see <c>UpcomingReleaseService</c>) without a
/// second request: it comes back in the same response as the books.
/// </summary>
public class AuthorBibliographyResult
{
    /// <summary>
    /// The source's own name for the author, or null when the source did not report one (a
    /// scraper that only implements <c>GetAuthorBooks</c>). A null name means "unknown", never
    /// "the author has no name" - callers must not propose a rename from it.
    /// </summary>
    public string? Name { get; }

    public IList<AuthorBookResult> Books { get; }

    public AuthorBibliographyResult(string? name, IList<AuthorBookResult> books)
    {
        Name = name;
        Books = books;
    }
}
