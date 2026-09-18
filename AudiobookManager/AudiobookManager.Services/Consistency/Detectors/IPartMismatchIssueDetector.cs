using AudiobookManager.Database.Models;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Services;

/// <summary>
/// Detects owned books of a matched series whose stored <c>SeriesPart</c> is missing or differs
/// from the position the series' roster assigns them. Like <see cref="IInitialsSpacingIssueDetector"/>
/// this is a library-wide sweep rather than an <see cref="IConsistencyIssueDetector"/> (which
/// checks one book's on-disk state): the finding comes from the *match* between a roster and the
/// series' owned books, so the whole series' reconciliation is the unit of work. Both here and in
/// the series detail, that reconciliation is the same cached computation
/// (<see cref="ISeriesReconciliationProvider.GetReconciliationAsync"/>), so the consistency screen and the
/// series detail report the same books and never drift apart.
///
/// The reconciliation is explicitly bounded (roster and owned-key caps) and throws
/// <see cref="InvalidOperationException"/> when a series is over a cap; every call here fails
/// soft on that - the series is logged and skipped, so one pathological series can never fail a
/// whole-consistency check. The cap breach is still visible on the series detail page itself,
/// where it is a hard failure.
/// </summary>
public interface IPartMismatchIssueDetector
{
    /// <summary>
    /// One issue per part mismatch across every matched series. Runs as the library-wide sweep of
    /// the full consistency check, which clears the issue table up front, so all emission is
    /// insert-only. The sweep reconciles the series one at a time - deliberately sequential, never
    /// fanned out with <c>Task.WhenAll</c> - because a cache-miss reconciliation computes inline
    /// through the caller's scoped database context, which must not be driven from concurrent
    /// tasks.
    /// </summary>
    Task<IReadOnlyList<ConsistencyIssue>> DetectLibraryWideAsync();

    /// <summary>
    /// The part mismatches for a single book, for the single-book recheck (which deletes and
    /// re-inserts only that book's issues). A book outside a matched series produces nothing.
    /// </summary>
    Task<IReadOnlyList<ConsistencyIssue>> DetectForAudiobookAsync(DbAudiobook audiobook);
}