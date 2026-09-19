using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// The computed result of reconciling a matched author's standalone-books roster
/// (<see cref="AudiobookManager.Database.Models.AuthorExpectedBook"/>) against the author's owned
/// standalone (no-series) books - the author-detail page's counterpart of
/// <see cref="SeriesReconciliation"/>. There is no ignored-list or part-mismatch section here: a
/// standalone book carries no series position to mismatch, and the roster has no ignore flow of
/// its own yet (see the design note this feature ships with).
/// </summary>
public sealed record AuthorReconciliation(
    IReadOnlyList<AuthorExpectedBookInfo> Missing,
    IReadOnlyList<AuthorExpectedBookInfo> Upcoming,
    int ExpectedBookCount,
    int OwnedCount)
{
    public int MissingBookCount => Missing.Count;
    public int UpcomingBookCount => Upcoming.Count;
}

/// <summary>One entry of an author's standalone-books roster, read-side shape - mirrors <see cref="SeriesExpectedBookInfo"/>.</summary>
public sealed class AuthorExpectedBookInfo
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? SourceUrl { get; set; }
    public bool IsIgnored { get; set; }
}
