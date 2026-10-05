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
    int? NumberOfRatings = null,
    /// <summary>Every series the source reported (<c>SeriesName</c>/<c>SeriesPart</c> are only the first). Empty when it reported none.</summary>
    IReadOnlyList<PendingRefreshSeriesDto>? Series = null,
    /// <summary>Keys of the book qualifiers the source reported (the book name is already clean of their wording). Null when none.</summary>
    IReadOnlyList<string>? Qualifiers = null);

/// <summary>One series a source reported for a book: the mapped name, its part, and the name before series mapping.</summary>
public record PendingRefreshSeriesDto(string SeriesName, string? SeriesPart, string? OriginalSeriesName);

/// <summary>One row of the pending-refresh list page.</summary>
public record PendingMetadataRefreshListItemDto(
    long AudiobookId,
    string BookName,
    IReadOnlyList<string> Authors,
    DateTime FetchedAt,
    string SourceName,
    IReadOnlyList<string> ChangedFields,
    List<string>? Qualifiers = null);

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
/// <remarks>
/// <paramref name="PrimarySeriesName"/> optionally picks which of the snapshot's series becomes the
/// book's primary one (the one in its tags and path). Left out, the primary is chosen the way a
/// bulk apply chooses it: the book's current primary if the source still lists it.
/// <paramref name="ReplaceExisting"/> authorizes overwriting a file that already occupies the path
/// the apply moves the book to (the user's answer to the duplicate-target dialog); without it such
/// an apply is refused with a 409.
/// </remarks>
public record ApplyPendingRefreshDto(
    List<string>? Fields, bool SplitTitleOnColon = false, string? PrimarySeriesName = null, bool ReplaceExisting = false);

/// <summary>The body of POST api/metadata-refresh/apply-selected.</summary>
public record ApplySelectedMetadataRefreshDto(List<long> AudiobookIds, bool SplitTitleOnColon = false);

/// <summary>The result of POST api/metadata-refresh/reevaluate.</summary>
public record MetadataRefreshReevaluateResultDto(int Processed, int Updated, int Removed, bool AutoApplyStarted);

/// <summary>The result of POST api/metadata-refresh/dismiss-selected.</summary>
public record DismissSelectedMetadataRefreshResultDto(int Dismissed);