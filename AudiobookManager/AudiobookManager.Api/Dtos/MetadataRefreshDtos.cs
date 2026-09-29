using AudiobookManager.Domain;

namespace AudiobookManager.Api.Dtos;

/// <summary>One differing field a metadata refresh would change.</summary>
public record MetadataRefreshDiffDto(string Field, string? LibraryValue, string? SourceValue);

/// <summary>The outcome of refreshing one book.</summary>
public record MetadataRefreshResultDto(
    bool Success,
    bool HasDifferences,
    IReadOnlyList<MetadataRefreshDiffDto> Differences,
    string? SourceName,
    string? Error);

/// <summary>The stored pending snapshot for a book, for the approval banner.</summary>
public record PendingMetadataRefreshDto(
    long AudiobookId,
    DateTime FetchedAt,
    string SourceName,
    string SourceUrl,
    PendingRefreshSnapshotDto Payload);

public record PendingRefreshSnapshotDto(
    string Url,
    string Source,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Narrators,
    string BookName,
    string? Subtitle,
    string? SeriesName,
    string? SeriesPart,
    int? Year,
    IReadOnlyList<string> Genres,
    string? Description,
    string? Language,
    string? Rating,
    string? Copyright,
    string? Publisher,
    string? Asin,
    int? NumberOfRatings = null);

/// <summary>One row of the pending-refresh list page.</summary>
public record PendingMetadataRefreshListItemDto(
    long AudiobookId,
    string BookName,
    IReadOnlyList<string> Authors,
    DateTime FetchedAt,
    string SourceName,
    IReadOnlyList<string> ChangedFields);

/// <summary>A page of the pending-refresh list (bounded; server-side paged).</summary>
public record PendingMetadataRefreshPageDto(IReadOnlyList<PendingMetadataRefreshListItemDto> Items, int Total);

/// <summary>The body of POST api/metadata-refresh/bulk.</summary>
public record BulkMetadataRefreshDto(DateTime? OlderThanUtc);

/// <summary>
/// The body of POST api/metadata-refresh/apply-filtered: apply the full pending snapshot to
/// every book whose stored changed-fields are entirely contained in <see cref="Fields"/> - the
/// same subset rule the pending list's filter uses to decide which rows match.
/// </summary>
public record BulkApplyFilteredMetadataRefreshDto(List<string> Fields, bool SplitTitleOnColon = false);

/// <summary>
/// The body of POST api/metadata-refresh/{id}/apply. Null or empty <see cref="Fields"/> applies
/// every field the stored snapshot recorded as changed; an explicit list applies only those.
/// <see cref="SplitTitleOnColon"/> defaults to false - a stored snapshot's BookName is the
/// source's raw, unsplit title (AudiobookManager.Services.TitleSplitter), so this opts in to
/// recovering "Title: Subtitle" from it rather than assuming every colon is a separator.
/// </summary>
public record ApplyPendingRefreshDto(List<string>? Fields, bool SplitTitleOnColon = false);

/// <summary>The body of POST api/metadata-refresh/apply-selected.</summary>
public record ApplySelectedMetadataRefreshDto(List<long> AudiobookIds, bool SplitTitleOnColon = false);

/// <summary>The result of POST api/metadata-refresh/reevaluate.</summary>
public record MetadataRefreshReevaluateResultDto(int Processed, int Updated, int Removed);

/// <summary>The result of POST api/metadata-refresh/dismiss-selected.</summary>
public record DismissSelectedMetadataRefreshResultDto(int Dismissed);