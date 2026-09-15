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

    Task<(int Processed, int Succeeded, int Failed)> AlignAuthorsAsync(
        List<string> sourceNames,
        string targetName,
        Func<int, int, int, int, Task> progressAction);

    Task<(int Processed, int Succeeded, int Failed)> AlignSeriesAsync(
        List<string> sourceValues,
        string targetValue,
        Func<int, int, int, int, Task> progressAction);
}
