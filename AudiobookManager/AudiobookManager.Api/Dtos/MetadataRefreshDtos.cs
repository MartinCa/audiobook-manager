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
    string? Asin);

/// <summary>One row of the pending-refresh list page.</summary>
public record PendingMetadataRefreshListItemDto(
    long AudiobookId,
    string BookName,
    IReadOnlyList<string> Authors,
    DateTime FetchedAt,
    string SourceName);

/// <summary>A page of the pending-refresh list (bounded; server-side paged).</summary>
public record PendingMetadataRefreshPageDto(IReadOnlyList<PendingMetadataRefreshListItemDto> Items, int Total);

/// <summary>The body of POST api/metadata-refresh/bulk.</summary>
public record BulkMetadataRefreshDto(DateTime? OlderThanUtc);