using AudiobookManager.Domain;

namespace AudiobookManager.Api.Dtos;

/// <summary>The outcome of refreshing one series from its matched source.</summary>
public record SeriesRefreshResultDto(
    bool Success,
    bool HasChanges,
    int ChangeCount,
    string? SourceName);

/// <summary>
/// One explicit change a pending series review shows. ChangeType is deliberately a string (e.g.
/// "PartUpdate"), not an enum, so it crosses the wire as itself - this API does not register a
/// string-enum converter; DESIGN.md wants string enums.
/// </summary>
public record SeriesRefreshChangeDto(
    string ChangeType,
    long? AudiobookId,
    string? BookName,
    string? StoredPart,
    string? NewPart,
    string? RosterTitle,
    string? Position,
    string? Title,
    int? Year);

/// <summary>The stored pending review for one series, for the review dialog.</summary>
public record SeriesRefreshPendingDto(
    string SeriesName,
    string SourceName,
    string SourceUrl,
    string? SourceSeriesName,
    DateTime FetchedAt,
    IReadOnlyList<SeriesRefreshChangeDto> Changes);

/// <summary>One row of the pending series-refresh list page.</summary>
public record SeriesRefreshPendingListItemDto(
    string SeriesName,
    string SourceName,
    string? SourceSeriesName,
    DateTime FetchedAt,
    int ChangeCount);

/// <summary>A page of the pending series-refresh list (bounded; server-side paged).</summary>
public record SeriesRefreshPendingPageDto(IReadOnlyList<SeriesRefreshPendingListItemDto> Items, int Total);

/// <summary>
/// One accepted change of a pending apply. ChangeType ("PartUpdate" | "MissingBook" |
/// "PartRemoval") selects the shape; for a part update or removal AudiobookId is the target
/// library book, for a missing book it is the library book the user chose to assign the roster
/// entry (Position/Title), and an omitted/zero AudiobookId means "skip".
/// </summary>
public class ApplySeriesRefreshChangeDto
{
    public string? ChangeType { get; set; }
    public long? AudiobookId { get; set; }
    public string? Position { get; set; }
    public string? Title { get; set; }
}

public class ApplySeriesRefreshRequestDto
{
    public bool AdoptSourceSeriesName { get; set; }
    public List<ApplySeriesRefreshChangeDto> Selections { get; set; } = new();
}

/// <summary>Mapping between the wire change-type strings and the domain enum.</summary>
internal static class SeriesRefreshChangeTypeDto
{
    public static string ToDto(SeriesRefreshChangeType type) => type switch
    {
        SeriesRefreshChangeType.PartUpdate => "PartUpdate",
        SeriesRefreshChangeType.MissingBook => "MissingBook",
        SeriesRefreshChangeType.PartRemoval => "PartRemoval",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static SeriesRefreshChangeType? FromDto(string? value) => value?.Trim() switch
    {
        "PartUpdate" => SeriesRefreshChangeType.PartUpdate,
        "MissingBook" => SeriesRefreshChangeType.MissingBook,
        "PartRemoval" => SeriesRefreshChangeType.PartRemoval,
        _ => null,
    };
}