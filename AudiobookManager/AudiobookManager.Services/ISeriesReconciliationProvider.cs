using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// The cached per-series reconciliation - which roster entries the library owns, which it is
/// missing, which the user ignored, and which owned books carry a part the roster contradicts.
///
/// This is deliberately its own interface rather than a member of <see cref="ISeriesService"/>,
/// and the reason is a dependency cycle rather than taste. The consistency graph reaches back
/// into series data: <c>LibraryConsistencyService</c> owns the part-mismatch detector, which
/// needs the reconciliation - while <c>SeriesService</c> needs the consistency service to recheck
/// a book after it rewrites one. Pointing the detector at the whole <see cref="ISeriesService"/>
/// closed that loop into
/// <c>SeriesService -&gt; ILibraryConsistencyService -&gt; IPartMismatchIssueDetector -&gt; ISeriesService</c>,
/// which the container refuses to construct: the API would not start at all under Development's
/// ValidateOnBuild, and in Production it started and then 500'd every /api/series request.
///
/// The computation needs none of that - only the series and audiobook repositories and the cache -
/// so the narrow interface both sides depend on is what breaks the loop honestly, without a lazy
/// resolve hiding the edge. Nothing that depends on this may take a dependency on
/// <see cref="ISeriesService"/> or <c>ILibraryConsistencyService</c>, or the cycle comes back.
/// </summary>
public interface ISeriesReconciliationProvider
{
    /// <summary>
    /// The series' reconciliation, computed once per series per change and shared by every
    /// consumer (the detail page's sections, the overview counts, the library-wide part-mismatch
    /// sweep and its resolver) so they cannot disagree about what a series is missing.
    /// </summary>
    Task<SeriesReconciliation> GetReconciliationAsync(string seriesName);
}
