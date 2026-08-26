using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// Read-side series catalog: browsing series across the whole library and reporting which
/// books of a matched series are missing.
///
/// This service never writes Author/Series/SeriesPart/Year/BookName onto an audiobook. It
/// only reads audiobooks and writes the parallel series catalog tables, so the "no DB-only
/// field updates" binding invariant does not apply to any code path here.
/// </summary>
public interface ISeriesService
{
    Task<List<SeriesOverview>> GetAllSeriesOverviewAsync();

    Task<SeriesDetail?> GetSeriesDetailAsync(string seriesName);

    Task<List<SeriesMatchCandidate>> SuggestSeriesMatchesAsync(string seriesName);

    /// <summary>
    /// Manual match lookup driven by user input rather than the library's own series name:
    /// <paramref name="query"/> is either a free-text search term (routed through the same
    /// source search as <see cref="SuggestSeriesMatchesAsync(string)"/>) or an absolute URL
    /// pointing directly at a series page on a supported source, in which case exactly that
    /// one series is returned (scored for display, not filtered out) instead of a search.
    /// </summary>
    Task<List<SeriesMatchCandidate>> SearchSeriesMatchesAsync(string seriesName, string query);

    Task<SeriesOverview> MatchSeriesAsync(string seriesName, string sourceName, string sourceSeriesId, double? confidence = null);

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
}
