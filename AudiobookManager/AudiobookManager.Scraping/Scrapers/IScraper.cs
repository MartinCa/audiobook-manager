using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Scraping.Scrapers;
public interface IScraper
{
    string SourceName { get; }

    bool RequiresApiKey => false;

    bool IsApiKeyConfigured => true;

    bool IsSource(string sourceName);

    bool SupportsUrl(string url);

    Task<IList<MetadataSearchResult>> Search(string searchTerm);

    Task<MetadataSearchResult> GetBookDetails(string bookUrl);

    /// <summary>
    /// Whether this source can look up a whole series roster (used by the series catalog
    /// to detect missing books). Optional capability - sources that only do per-book
    /// lookups leave the defaults below in place.
    /// </summary>
    bool SupportsSeriesLookup => false;

    Task<IList<SeriesSearchResult>> SearchSeries(string searchTerm) =>
        Task.FromResult<IList<SeriesSearchResult>>(new List<SeriesSearchResult>());

    /// <summary>
    /// Fetches a series and its full book roster by the source id returned from
    /// <see cref="SearchSeries"/> (a source URL is also accepted).
    /// </summary>
    Task<SeriesSearchResult?> GetSeriesBooks(string seriesIdOrUrl) =>
        Task.FromResult<SeriesSearchResult?>(null);

    /// <summary>
    /// Whether this source can look up authors by id and report their upcoming releases.
    /// Optional capability, like <see cref="SupportsSeriesLookup"/> - sources that don't
    /// support it leave the defaults below in place.
    /// </summary>
    bool SupportsAuthorLookup => false;

    Task<IList<AuthorSearchResult>> SearchAuthors(string searchTerm) =>
        Task.FromResult<IList<AuthorSearchResult>>(new List<AuthorSearchResult>());

    /// <summary>
    /// Not-yet-released books credited to the author identified by <paramref name="authorSourceId"/>
    /// (the id returned from <see cref="SearchAuthors"/>).
    /// </summary>
    Task<IList<UpcomingReleaseResult>> GetAuthorUpcomingReleases(string authorSourceId) =>
        Task.FromResult<IList<UpcomingReleaseResult>>(new List<UpcomingReleaseResult>());

    /// <summary>
    /// The author's full bibliography - not just not-yet-released books - identified by
    /// <paramref name="authorSourceId"/> (the id returned from <see cref="SearchAuthors"/>).
    /// Backs the author standalone-books roster; a book this returns with
    /// <see cref="AuthorBookResult.HasSeries"/> set is left out of that roster by the caller (it
    /// is already covered through its series' own roster). Optional capability, defaulting to
    /// empty like <see cref="GetAuthorUpcomingReleases"/> - gated by the same
    /// <see cref="SupportsAuthorLookup"/> flag.
    /// </summary>
    Task<IList<AuthorBookResult>> GetAuthorBooks(string authorSourceId) =>
        Task.FromResult<IList<AuthorBookResult>>(new List<AuthorBookResult>());

    /// <summary>
    /// Not-yet-released books in the series identified by <paramref name="seriesSourceId"/> (the
    /// same source id <see cref="SeriesSearchResult.SourceId"/>/<see cref="GetSeriesBooks"/> use).
    /// </summary>
    Task<IList<UpcomingReleaseResult>> GetSeriesUpcomingReleases(string seriesSourceId) =>
        Task.FromResult<IList<UpcomingReleaseResult>>(new List<UpcomingReleaseResult>());
}
