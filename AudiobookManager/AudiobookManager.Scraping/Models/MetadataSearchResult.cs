using AudiobookManager.Domain;
using AudiobookManager.Scraping.Utils;

namespace AudiobookManager.Scraping.Models;
public class MetadataSearchResult
{
    public string Url { get; set; }
    public string Source { get; set; } = "";
    public IList<Person> Authors { get; set; }
    public IList<Person> Narrators { get; set; }
    public string BookName { get; set; }
    public string? Subtitle { get; set; }
    public string? Duration { get; set; }
    public int? Year { get; set; }
    public string? Language { get; set; }
    public string? ImageUrl { get; set; }
    public IList<MetadataSeriesSearchResult>? Series { get; set; }
    public string? Description { get; set; }
    public IList<string> Genres { get; set; }
    public float? Rating { get; set; }
    public int? NumberOfRatings { get; set; }
    public string? Copyright { get; set; }
    public string? Publisher { get; set; }
    public string? Asin { get; set; }
    public string? Isbn { get; set; }

    /// <summary>
    /// The canonical form of <see cref="Url"/> - scheme/host/path only, with tracking/session
    /// query parameters (Audible's ref=/pf_rd_*, Goodreads' qid=/from_search=, etc.) stripped.
    /// This is what a caller should persist (e.g. into Audiobook.Www); Url itself stays a
    /// faithful record of what was actually fetched, so a caller matching a result back to the
    /// request that produced it isn't defeated by the model canonicalizing its own identity.
    /// </summary>
    public string CleanUrl => BookUrlCleaner.Clean(Url);

    public MetadataSearchResult(string url, string bookName)
    {
        Url = url;
        BookName = bookName;

        Authors = new List<Person>();
        Narrators = new List<Person>();
        Genres = new List<string>();
    }
}
