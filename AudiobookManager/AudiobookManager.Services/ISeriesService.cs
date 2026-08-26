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

    Task<SeriesOverview> MatchSeriesAsync(string seriesName, string sourceName, string sourceSeriesId, double? confidence = null);

    Task<(int Processed, int Succeeded, int Failed)> BulkAutoMatchSeriesAsync(
        double confidenceThreshold,
        List<string>? seriesNames,
        Func<int, int, int, int, Task> progressAction);

    Task<(int Processed, int Succeeded, int Failed)> RefreshSeriesAsync(
        string seriesName,
        Func<int, int, int, int, Task> progressAction);

    Task<(int Processed, int Succeeded, int Failed)> RefreshAllSeriesAsync(
        Func<int, int, int, int, Task> progressAction);

    Task IgnoreExpectedBookAsync(long expectedBookId, bool ignored);
}
