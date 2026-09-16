namespace AudiobookManager.Domain;

/// <summary>
/// The kind of explicit change a series refresh computed between the freshly fetched source
/// roster and the series' current library books. The three shapes together are what the pending
/// review dialog renders and what the pending apply acts on:
/// <list type="bullet">
/// <item><see cref="PartUpdate"/> - an owned book matched a roster entry whose position differs
/// from the book's stored part (e.g. a book at part "01" that the source now positions at "02").</item>
/// <item><see cref="MissingBook"/> - a roster entry the library does not own (the source knows a
/// book the user does not).</item>
/// <item><see cref="PartRemoval"/> - an owned book carrying a part that no fetched roster entry
/// corresponds to (the source no longer lists the book, so its part no longer has a basis).</item>
/// </list>
/// </summary>
public enum SeriesRefreshChangeType
{
    PartUpdate,
    MissingBook,
    PartRemoval,
}

/// <summary>
/// One explicit change a series refresh produced. <see cref="AudiobookId"/> is the target
/// library book for <see cref="SeriesRefreshChangeType.PartUpdate"/> /
/// <see cref="SeriesRefreshChangeType.PartRemoval"/>; a missing book is addressed by its roster
/// natural key (<see cref="Position"/>/<see cref="Title"/>) because it may have no library book
/// yet - the user picks the library book to assign at apply time.
/// </summary>
public record SeriesRefreshChange(
    SeriesRefreshChangeType Type,
    long? AudiobookId,
    string? BookName,
    string? StoredPart,
    string? NewPart,
    string? RosterTitle,
    string? Position,
    string? Title,
    int? Year);

/// <summary>
/// The outcome of refreshing one series from its matched source. The fetch either succeeds - in
/// which case the check is stamped regardless of whether anything changed - or it throws, and the
/// controller reports that failure with the usual problem conventions.
///
/// <see cref="SourceName"/> is the metadata source's name (e.g. "Hardcover"), the same value the
/// catalog row and the pending snapshot store - never the source's own series title, which is a
/// separate value (<see cref="PendingSeriesRefresh.SourceSeriesName"/>, the catalog row's
/// <c>MatchedSeriesName</c>).
/// </summary>
public record SeriesRefreshResult(
    bool Success,
    bool HasChanges,
    int ChangeCount,
    string? SourceName);

/// <summary>
/// The stored pending refresh for one series: the source snapshot plus the explicit changes the
/// review applies. One row per series - a fresh refresh supersedes the old snapshot.
/// </summary>
public record PendingSeriesRefresh(
    string SeriesName,
    DateTime FetchedAt,
    string SourceName,
    string SourceUrl,
    string? SourceSeriesName,
    IReadOnlyList<SeriesRefreshChange> Changes,
    IReadOnlyList<SeriesRefreshRosterEntry> Roster);

/// <summary>One row of the pending series-refresh list page.</summary>
public record PendingSeriesRefreshListItem(
    string SeriesName,
    string SourceName,
    string? SourceSeriesName,
    DateTime FetchedAt,
    int ChangeCount);

/// <summary>One entry of the fetched roster snapshot stored in a pending series refresh.</summary>
public record SeriesRefreshRosterEntry(
    string? Position,
    string Title,
    int? Year,
    string? SourceUrl,
    bool IsCompilation);

/// <summary>
/// One accepted change in a pending series refresh apply. For
/// <see cref="SeriesRefreshChangeType.PartUpdate"/>/<see cref="SeriesRefreshChangeType.PartRemoval"/>
/// <see cref="AudiobookId"/> is the target library book; for
/// <see cref="SeriesRefreshChangeType.MissingBook"/> it is the library book the user chose to
/// assign the roster entry to (the entry itself is addressed by <see cref="Position"/>/<see cref="Title"/>).
/// </summary>
public record SeriesRefreshApplyChange(
    SeriesRefreshChangeType Type,
    long? AudiobookId,
    string? Position,
    string? Title);

/// <summary>
/// The complete pending apply request: the accepted change selections plus the optional adoption
/// of the source's own series name. Applied changes rewrite each book through
/// <see cref="IAudiobookService.UpdateAudiobook"/> under the per-audiobook save gate - never as
/// a DB-only field write.
/// </summary>
public record SeriesRefreshApplyRequest(
    bool AdoptSourceSeriesName,
    IReadOnlyList<SeriesRefreshApplyChange> Selections);