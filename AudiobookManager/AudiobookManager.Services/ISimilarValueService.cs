using AudiobookManager.Domain;

namespace AudiobookManager.Services;

public interface ISimilarValueService
{
    /// <summary>
    /// One page of the detected near-duplicate groups, with the total number of groups. The
    /// clustering still runs over the whole distinct-value set on every request (the detection is
    /// stateless by design), but only the requested page of groups - each carrying per-candidate
    /// book counts, not book lists - crosses the wire or reaches the DOM. The group order is
    /// deterministic so paging stays stable between requests.
    /// </summary>
    Task<(List<SimilarValueGroup> Items, int Total)> DetectSimilarAuthorsAsync(int skip, int take);
    Task<(List<SimilarValueGroup> Items, int Total)> DetectSimilarSeriesAsync(int skip, int take);

    /// <summary>
    /// Classifies one typed author/series entry against the library's existing values: Exact when
    /// the same value already exists (accent- and case-insensitive), Similar when only near
    /// matches exist, New when neither. Bounded by construction: one input value, a capped
    /// candidate prefilter, and a result capped at <paramref name="limit"/>. Backs the explicit
    /// exact/new/similar indicators on the author and series entry fields.
    /// </summary>
    Task<EntryValueStatus> GetEntryStatusAsync(EntryValueKind kind, string value, int limit);

    /// <summary>Every ignored pair for one kind ("authors"/"series"), oldest first.</summary>
    Task<List<IgnoredSimilarValuePairInfo>> GetIgnoredPairsAsync(string kind);

    /// <summary>
    /// Marks <paramref name="value"/> as not similar to each of <paramref name="againstValues"/>
    /// (typically the rest of its current detected group), then invalidates the detection cache
    /// so the group re-splits immediately rather than after the cache TTL. Returns false without
    /// writing anything if <paramref name="value"/> or any entry of <paramref name="againstValues"/>
    /// is not a currently-existing value for <paramref name="kind"/> - a stale client (an old tab
    /// still holding a group that alignment has since folded away) must not be able to accumulate
    /// ignored-pair rows for values nothing in the library carries any more.
    /// </summary>
    Task<bool> IgnorePairAsync(string kind, string value, List<string> againstValues);

    /// <summary>Removes one ignored pair by id and invalidates the detection cache.</summary>
    Task RemoveIgnoredPairAsync(string kind, long id);

    Task<(int Processed, int Succeeded, int Failed)> AlignAuthorsAsync(
        List<string> sourceNames,
        string targetName,
        Func<int, int, int, int, Task> progressAction);

    Task<(int Processed, int Succeeded, int Failed)> AlignSeriesAsync(
        List<string> sourceValues,
        string targetValue,
        Func<int, int, int, int, Task> progressAction);

    /// <summary>
    /// Renames the author <paramref name="oldName"/> to <paramref name="newName"/> on every book
    /// that has them, through <see cref="IAudiobookService.UpdateAudiobook"/> per book (the same
    /// alignment pipeline similar-value merging uses, so tags, folders and sidecars move with the
    /// rename), and then - only when every book succeeded - carries the author's source match,
    /// follow and roster over to the new name (see <see cref="Database.Repositories.IPersonRepository.MergeAuthorAsync"/>).
    /// A name that already exists is a merge: its own match wins. A partial failure leaves the
    /// old author in place for the still-unrenamed books, so retrying finishes the job.
    /// </summary>
    Task<(int Processed, int Succeeded, int Failed)> RenameAuthorAsync(
        string oldName,
        string newName,
        Func<int, int, int, int, Task> progressAction);

    /// <summary>
    /// Renames the series <paramref name="oldName"/> to <paramref name="newName"/> on every book
    /// that lists it (primary or additional) through the same per-book pipeline, then - only when
    /// every book succeeded - moves the series' catalog row (roster, match, mapping patterns,
    /// follow) to the new name and drops its pending refresh review. The name is the clean series
    /// value: each book's own qualifiers are re-applied as the suffix when its files are written,
    /// so a "Jack Reacher (Dramatized)" book follows a rename of "Jack Reacher" to
    /// "Reacher (Dramatized)" without the qualifier ever being part of the stored name.
    /// </summary>
    Task<(int Processed, int Succeeded, int Failed)> RenameSeriesAsync(
        string oldName,
        string newName,
        Func<int, int, int, int, Task> progressAction);

    /// <summary>
    /// The reason a series rename cannot go ahead, or null when it can: the name rules of
    /// <see cref="ValueRenameRules.ValidateSeries"/>, plus a refusal when BOTH names already own
    /// a catalog row. A rename would then have to merge two rosters (and their mapping patterns,
    /// omnibus flag and follow), and book renames cannot be unwound - the same refusal the
    /// source-series-name adoption makes. Checked before any book is touched, so the controller
    /// can fail the request synchronously instead of discovering it in the background.
    /// </summary>
    Task<string?> ValidateSeriesRenameAsync(string oldName, string newName);
}
