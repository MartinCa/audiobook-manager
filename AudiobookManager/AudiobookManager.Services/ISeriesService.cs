using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// Read-side series catalog: browsing series across the whole library and reporting which
/// books of a matched series are missing.
///
/// Series/SeriesPart change on an audiobook in exactly one place here: ApplyMissingBookAsync,
/// which applies a series assignment to a chosen audiobook through AudiobookService.UpdateAudiobook
/// - the path the "no DB-only field updates" binding invariant requires, so the m4b tags, library
/// path, sidecars and the database all update together. Every other code path only reads
/// audiobooks and writes the parallel series catalog tables.
/// </summary>
public interface ISeriesService
{
    Task<List<SeriesOverview>> GetAllSeriesOverviewAsync();

    /// <summary>
    /// One page of the series overview, in the total order the overview is displayed in. The
    /// page and its total come from SQL; only the page's series values are hydrated into
    /// overviews, so the read is proportional to the rendered rows, not the library. See
    /// <see cref="AudiobookRepository.GetSeriesValuesPageAsync"/> for the search/matched filters.
    /// </summary>
    Task<SeriesOverviewPage> GetSeriesOverviewPageAsync(int page, int pageSize, string? search, bool? matched);

    /// <summary>Total/matched/unmatched series counts for the overview header badge.</summary>
    Task<SeriesOverviewCounts> GetSeriesOverviewCountsAsync();

    /// <summary>
    /// One page per section of the series detail: the overview plus one page of owned, missing
    /// and ignored books, each with its full total. Per request the reads are bounded: one catalog
    /// metadata row, one SQL page of owned books, and the cached reconciliation. The
    /// reconciliation itself (classifying the roster - the metadata source's stored series page,
    /// hard-capped by <c>SeriesService.MaxReconciliationRosterEntries</c> - against the series'
    /// owned position/title keys, capped too) is computed once per series per change by
    /// <c>ISeriesReconciliationCache</c>, never per page request, so a section request never
    /// materializes the roster plus every owned book of the series.
    /// </summary>
    Task<SeriesDetailPage?> GetSeriesDetailPageAsync(
        string seriesName,
        int ownedSkip, int ownedTake,
        int missingSkip, int missingTake,
        int ignoredSkip, int ignoredTake);

    Task<List<SeriesMatchCandidate>> SuggestSeriesMatchesAsync(string seriesName);

    /// <summary>
    /// Manual match lookup driven by user input rather than the library's own series name:
    /// <paramref name="query"/> is either a free-text search term (routed through the same
    /// source search as <see cref="SuggestSeriesMatchesAsync(string)"/>) or an absolute URL
    /// pointing directly at a series page on a supported source, in which case exactly that
    /// one series is returned (scored for display, not filtered out) instead of a search.
    /// </summary>
    Task<List<SeriesMatchCandidate>> SearchSeriesMatchesAsync(string seriesName, string query);

    Task<SeriesOverview> MatchSeriesAsync(string seriesName, string sourceName, string sourceSeriesId, double? confidence = null, bool includeOmnibusEditions = false);

    /// <summary>
    /// Updates the per-series omnibus/box-set display setting. The full roster (compilations
    /// included) is always stored regardless of this setting, so this is a pure DB write - no
    /// re-fetch from the matched source is needed to apply it.
    /// </summary>
    Task<SeriesOverview> SetIncludeOmnibusEditionsAsync(string seriesName, bool includeOmnibusEditions);

    /// <summary>
    /// StopReason is set (and the batch stops early) only when the Hardcover daily request
    /// budget is exhausted mid-run - every other per-item failure is folded into Failed and
    /// the batch keeps going.
    /// </summary>
    Task<(int Processed, int Succeeded, int Failed, string? StopReason)> BulkAutoMatchSeriesAsync(
        double confidenceThreshold,
        List<string>? seriesNames,
        Func<int, int, int, int, Task> progressAction);

    Task<(int Processed, int Succeeded, int Failed, string? StopReason)> RefreshSeriesAsync(
        string seriesName,
        Func<int, int, int, int, Task> progressAction);

    Task<(int Processed, int Succeeded, int Failed, string? StopReason)> RefreshAllSeriesAsync(
        Func<int, int, int, int, Task> progressAction);

    /// <summary>
    /// Flips the ignore flag on a roster entry, addressed by its natural key (series name
    /// plus position and/or title) rather than its row id: matching and refreshing delete and
    /// re-insert the roster, so a row id a client cached earlier can refer to a different
    /// book by the time the call arrives.
    /// </summary>
    Task IgnoreExpectedBookAsync(string seriesName, string? position, string? title, bool ignored);

    /// <summary>
    /// Library audiobooks that might be a given missing expected book, ranked: a close match on
    /// author AND book name first, then a close match on book name alone. The expected book is
    /// addressed by its natural key (position and/or title) like the ignore endpoints; the
    /// returned list is capped at a small constant.
    /// </summary>
    Task<List<SeriesBookCandidate>> FindMissingBookCandidatesAsync(string seriesName, string? position, string? title);

    /// <summary>
    /// Applies the series name and the roster entry's position to a chosen audiobook, through
    /// AudiobookService.UpdateAudiobook. The caller holds the per-audiobook save gate around this
    /// call - the gate is non-reentrant, so this method must never take it itself.
    /// </summary>
    Task ApplyMissingBookAsync(string seriesName, string? position, string? title, long audiobookId);
}
