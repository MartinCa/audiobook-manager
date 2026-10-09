namespace AudiobookManager.Database.Repositories;

/// <summary>One selectable queue state: the stable wire value and the text the filter shows.</summary>
public record QueueStateOption(string Value, string Label);

/// <summary>
/// The "Queue status" filter every library list offers (books, series, authors): where an item
/// sits in the review queues that online-metadata work moves it through. A book that was
/// bulk-searched stays "unsupported" (no matched source) until its result is picked and applied,
/// so without this filter the same books come back every time the unsupported list is worked
/// through.
///
/// The values are wire strings stored in saved filter presets, so they never change meaning.
/// Selecting several is a union ("any of these"); <c>NotQueued</c> means "in none of the queues
/// below" and is combined with the others the same way, so "not queued or rejected" re-offers
/// rejected books.
/// </summary>
public static class QueueState
{
    /// <summary>In none of the queues listed for the entity - the normal, untouched state.</summary>
    public const string NotQueued = "NotQueued";

    /// <summary>Books: a bulk online search ran and its result still waits for a pick.</summary>
    public const string MatchPending = "MatchPending";

    /// <summary>Books: a bulk online search result was rejected (the Failed/Rejected list).</summary>
    public const string MatchRejected = "MatchRejected";

    /// <summary>
    /// A refresh result waits for review: a book's pending metadata, a series' pending roster
    /// changes. (Authors have <see cref="RenamePending"/> instead.)
    /// </summary>
    public const string RefreshPending = "RefreshPending";

    /// <summary>Series and authors: the most recent refresh failed (the consistency issue list).</summary>
    public const string RefreshFailed = "RefreshFailed";

    /// <summary>Authors: the source spells the name differently and the rename awaits review.</summary>
    public const string RenamePending = "RenamePending";

    public static readonly IReadOnlyList<QueueStateOption> ForBooks =
    [
        new(NotQueued, "Not in any queue"),
        new(MatchPending, "Online match awaiting a pick"),
        new(MatchRejected, "Online match rejected"),
        new(RefreshPending, "Refresh awaiting review"),
    ];

    public static readonly IReadOnlyList<QueueStateOption> ForSeries =
    [
        new(NotQueued, "Not in any queue"),
        new(RefreshPending, "Refresh awaiting review"),
        new(RefreshFailed, "Refresh failed"),
    ];

    public static readonly IReadOnlyList<QueueStateOption> ForAuthors =
    [
        new(NotQueued, "Not in any queue"),
        new(RenamePending, "Name change awaiting review"),
        new(RefreshFailed, "Refresh failed"),
    ];
}
