namespace AudiobookManager.Database.Repositories;

/// <summary>
/// Additional narrowing for <see cref="IAudiobookRepository.GetSeriesValuesPageAsync"/>, layered
/// on top of the existing <c>search</c>/<c>matched</c>/<c>authorId</c> parameters. Every field is
/// optional and independent - a null field applies no constraint. <see cref="HasMissingBooks"/>
/// and <see cref="HasUpcomingBooks"/> depend on the fuzzy roster-vs-owned reconciliation
/// (<c>SeriesRosterMatcher</c>), which lives in the Services layer this repository does not
/// reference, so the repository does not evaluate them itself - see
/// <see cref="IAudiobookRepository.GetSeriesValuesPageAsync"/>'s <c>restrictToNames</c> parameter,
/// which <c>SeriesService</c> populates from that reconciliation before calling in.
/// </summary>
public record SeriesOverviewFilter(
    bool? Followed = null,
    int? MinOwnedBooks = null,
    int? MaxOwnedBooks = null,
    bool? HasMissingBooks = null,
    bool? HasUpcomingBooks = null,
    DateTime? RefreshedAfter = null,
    DateTime? RefreshedBefore = null,
    bool? NeverRefreshed = null)
{
    /// <summary>Whether this filter is a strict no-op - lets a caller skip building it up at all.</summary>
    public bool IsEmpty =>
        Followed is null && MinOwnedBooks is null && MaxOwnedBooks is null &&
        HasMissingBooks is null && HasUpcomingBooks is null &&
        RefreshedAfter is null && RefreshedBefore is null && NeverRefreshed is null;

    /// <summary>Whether resolving this filter needs the fuzzy roster reconciliation (a whole-library computation - see the type doc).</summary>
    public bool NeedsReconciliation => HasMissingBooks is not null || HasUpcomingBooks is not null;
}
