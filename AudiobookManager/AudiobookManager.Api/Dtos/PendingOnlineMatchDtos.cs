using System.ComponentModel.DataAnnotations;

namespace AudiobookManager.Api.Dtos;

/// <summary>The body of POST api/pending-online-match/search-selected.</summary>
public class BulkOnlineMatchSearchDto
{
    [Required] public List<long> AudiobookIds { get; set; } = null!;

    [Required] public List<string> SourceNames { get; set; } = null!;
}

/// <summary>The body of POST api/pending-online-match/{id}/select.</summary>
public record SelectOnlineMatchResultDto(int ResultIndex);

/// <summary>
/// One row of the Pending or Failed/Rejected list. Reuses <see cref="PendingRefreshSnapshotDto"/>
/// for each candidate result - the same shape the pending metadata-refresh banner renders, so the
/// frontend's existing snapshot-to-search-result conversion (and its result-card presentation)
/// applies here unchanged.
/// </summary>
public record PendingOnlineMatchListItemDto(
    long AudiobookId,
    string BookName,
    IReadOnlyList<string> Authors,
    DateTime SearchedAt,
    IReadOnlyList<string> SourceNames,
    IReadOnlyList<PendingRefreshSnapshotDto> Results);

/// <summary>A page of the Pending or Failed/Rejected list (bounded; server-side paged).</summary>
public record PendingOnlineMatchPageDto(IReadOnlyList<PendingOnlineMatchListItemDto> Items, int Total);
