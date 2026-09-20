namespace AudiobookManager.Database.Repositories;

/// <summary>
/// Additional narrowing for <see cref="IPersonRepository.GetAuthorSummariesPagedAsync"/>, the
/// author-list counterpart of <see cref="SeriesOverviewFilter"/>. <see cref="HasMissingBooks"/>
/// and <see cref="HasUpcomingBooks"/> are resolved the same way that type's do - the fuzzy
/// roster-vs-owned reconciliation lives in the Services layer, so this repository does not
/// evaluate those two fields itself; the caller (BrowseController, via
/// IAuthorReconciliationProvider) restricts the id set before it reaches the repository.
/// </summary>
public record AuthorSummaryFilter(
    bool? Followed = null,
    int? MinBookCount = null,
    int? MaxBookCount = null,
    bool? HasMissingBooks = null,
    bool? HasUpcomingBooks = null,
    bool? Matched = null,
    DateTime? RefreshedAfter = null,
    DateTime? RefreshedBefore = null,
    bool? NeverRefreshed = null,
    IReadOnlyCollection<string>? Sources = null)
{
    /// <summary>
    /// Synthetic <see cref="Sources"/> value for an unmatched author - there is no source-capable
    /// scraper an unmatched author's MatchedSourceName could ever equal, so this sentinel is
    /// handled separately from real source names wherever <see cref="Sources"/> is evaluated.
    /// </summary>
    public const string UnsupportedSource = "Unsupported";

    public bool IsEmpty =>
        Followed is null && MinBookCount is null && MaxBookCount is null &&
        HasMissingBooks is null && HasUpcomingBooks is null && Matched is null &&
        RefreshedAfter is null && RefreshedBefore is null && NeverRefreshed is null &&
        (Sources is null || Sources.Count == 0);

    public bool NeedsReconciliation => HasMissingBooks is not null || HasUpcomingBooks is not null;
}
