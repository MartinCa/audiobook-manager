using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using Microsoft.Extensions.Logging;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Services;

/// <summary>
/// Maps the cached per-series reconciliation's part mismatches to consistency issues. See
/// <see cref="IPartMismatchIssueDetector"/> for why this is a library-wide sweep and not an
/// <see cref="IConsistencyIssueDetector"/>. The per-series work is delegated to
/// <see cref="ISeriesReconciliationProvider.GetReconciliationAsync(string)"/> - never
/// reimplemented here - so the detected findings always agree with what the series detail renders,
/// and a full check that follows a series-detail browse reuses the cache instead of recomputing.
///
/// It depends on that narrow interface rather than <c>ISeriesService</c> on purpose: the wide one
/// pulls in <c>ILibraryConsistencyService</c>, which owns this detector, and the resulting cycle
/// left the container unable to construct it at all.
/// </summary>
public class PartMismatchIssueDetector : IPartMismatchIssueDetector
{
    private readonly ISeriesRepository _seriesRepository;
    private readonly ISeriesReconciliationProvider _reconciliation;
    private readonly ILogger<PartMismatchIssueDetector> _logger;

    public PartMismatchIssueDetector(
        ISeriesRepository seriesRepository,
        ISeriesReconciliationProvider reconciliation,
        ILogger<PartMismatchIssueDetector> logger)
    {
        _seriesRepository = seriesRepository;
        _reconciliation = reconciliation;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConsistencyIssue>> DetectLibraryWideAsync()
    {
        // One issue per treated book, bounded by the matched-series list (the sweep is groupable
        // only by series, and unmatched series have no roster to mismatch against).
        var matchedSeries = await _seriesRepository.GetMatchedSeriesNamesAsync();
        var issues = new List<ConsistencyIssue>();

        // Sequential by design, not an oversight. A reconciliation cache miss computes inline in
        // the caller (see SeriesReconciliationCache.GetOrComputeAsync) through the caller's scoped
        // DatabaseContext - the full check runs in a background operation's scope, not a request -
        // on its single SQLite connection. Fanning the series out with Task.WhenAll would run
        // those cache-miss computations concurrently against that one context - EF Core forbids
        // concurrent use of a DbContext, and SQLite serializes its readers anyway - so the sweep
        // awaits each series in turn.
        foreach (var seriesName in matchedSeries)
        {
            try
            {
                var reconciliation = await _reconciliation.GetReconciliationAsync(seriesName);
                foreach (var mismatch in reconciliation.PartMismatches)
                {
                    issues.Add(ToIssue(mismatch));
                }
            }
            catch (InvalidOperationException ex)
            {
                // A series over the bounded-reconciliation caps cannot be reconciled - the series
                // detail already fails loudly on it. Failing soft here keeps one pathological
                // series from failing the whole-consistency check.
                _logger.LogWarning(
                    ex, "Skipping series-part-mismatch sweep for series {SeriesName}: {Message}",
                    seriesName, ex.Message);
            }
        }

        return issues;
    }

    public async Task<IReadOnlyList<ConsistencyIssue>> DetectForAudiobookAsync(DbAudiobook audiobook)
    {
        if (string.IsNullOrWhiteSpace(audiobook.Series))
        {
            return new List<ConsistencyIssue>();
        }

        try
        {
            var reconciliation = await _reconciliation.GetReconciliationAsync(audiobook.Series);
            return reconciliation.PartMismatches
                .Where(m => m.AudiobookId == audiobook.Id)
                .Select(ToIssue)
                .ToList();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex, "Skipping series-part-mismatch detection for audiobook {AudiobookId} in series {SeriesName}: {Message}",
                audiobook.Id, audiobook.Series, ex.Message);
            return new List<ConsistencyIssue>();
        }
    }

    /// <summary>
    /// The issue's expected/actual values carry the roster position vs the stored part (the
    /// resolver writes <see cref="ConsistencyIssue.ExpectedValue"/> back into the book), and the
    /// description names the roster title the book was matched against, mirroring what the series
    /// detail's Part Mismatches section shows.
    /// </summary>
    private static ConsistencyIssue ToIssue(SeriesPartMismatch mismatch) => new()
    {
        AudiobookId = mismatch.AudiobookId,
        IssueType = ConsistencyIssueType.SeriesPartMismatch,
        Description =
            $"The stored series part of '{mismatch.BookName}' is missing or differs from "
            + $"part {mismatch.ExpectedPart} assigned to '{mismatch.RosterTitle}' in the matched series.",
        ExpectedValue = mismatch.ExpectedPart,
        ActualValue = mismatch.StoredPart,
        DetectedAt = DateTime.UtcNow
    };
}