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
    /// Backs the unified expected-books roster, so series books are INCLUDED (not filtered out)
    /// with their placement, and
    /// <see cref="AuthorBookResult.SeriesName"/>/<see cref="AuthorBookResult.SeriesSourceId"/>/
    /// <see cref="AuthorBookResult.SeriesPosition"/> carry the series placement for books that
    /// have one. Optional capability, defaulting to empty like
    /// <see cref="GetAuthorUpcomingReleases"/> - gated by the same
    /// <see cref="SupportsAuthorLookup"/> flag.
    ///
    /// Throws <c>AuthorNotFoundException</c> (AudiobookManager.Scraping) when the author cannot
    /// be resolved - the id does not parse, or the source returns no such author (deleted/merged
    /// upstream or a transient empty response). A caller refreshing an author's roster must treat
    /// this as a FAILED fetch and leave the stored roster untouched: an empty failure is not an
    /// empty bibliography, and pruning the roster to it would delete the author's stored books.
    /// A genuinely empty book list for an author the source DOES resolve must still come back as
    /// an empty result (not an exception), so the caller's normal prune-to-empty stays legal.
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
