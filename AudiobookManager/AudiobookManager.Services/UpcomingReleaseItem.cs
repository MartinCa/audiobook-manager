namespace AudiobookManager.Services;

/// <summary>
/// Where an <see cref="UpcomingReleaseItem"/> came from, and therefore how a "remove from upcoming
/// releases" action for it must be carried out - see the type's own doc comment.
/// </summary>
public enum UpcomingReleaseSource
{
    /// <summary>
    /// A real row in the legacy <c>upcoming_releases</c> scrape table (<see cref="AudiobookManager.Database.Models.UpcomingRelease"/>).
    /// Removed with the existing <c>DELETE api/UpcomingReleases/{id}</c>, keyed on <see cref="UpcomingReleaseItem.Id"/>.
    /// </summary>
    Legacy,

    /// <summary>
    /// Derived live from a followed-and-matched series' or author's roster (<see cref="AudiobookManager.Database.Models.SeriesExpectedBook"/>/
    /// <see cref="AudiobookManager.Database.Models.AuthorExpectedBook"/>), classified <c>Upcoming</c> by
    /// <see cref="AudiobookManager.Domain.ExpectedBookClassifier"/>. There is no backing
    /// <c>UpcomingRelease</c> row to delete - "remove" instead sets <c>IsIgnored</c> on the roster
    /// entry, the same mechanism the series/author detail pages already use to dismiss a roster
    /// entry. Addressed by <see cref="UpcomingReleaseItem.SeriesName"/>/<see cref="UpcomingReleaseItem.SeriesPosition"/>/
    /// <see cref="UpcomingReleaseItem.Title"/> (series-derived) or <see cref="UpcomingReleaseItem.AuthorId"/>/
    /// <see cref="UpcomingReleaseItem.Title"/> (author-derived) - the same natural-key addressing
    /// the roster ignore endpoints already use, since roster row ids are not stable across a
    /// re-match or refresh.
    /// </summary>
    Roster,
}

/// <summary>
/// One row of the unified "what's coming up for what I follow" view <see cref="IUpcomingReleaseService.GetUpcomingReleasesAsync"/>
/// returns - a union of the legacy scrape-and-store <c>UpcomingRelease</c> table and every
/// followed-and-matched series'/author's roster entries classified <c>Upcoming</c> (see
/// AudiobookManager/UPCOMING_RELEASES_DESIGN.md). <see cref="Source"/> tells the caller which
/// removal mechanism applies - see <see cref="UpcomingReleaseSource"/>.
/// </summary>
public sealed record UpcomingReleaseItem(
    UpcomingReleaseSource Source,
    /// <summary>The <c>UpcomingRelease</c> row id - set only for <see cref="UpcomingReleaseSource.Legacy"/>.</summary>
    long? Id,
    string Title,
    /// <summary>A precise release date, when known. Roster entries stored before a refresh added one, or that only ever had a <see cref="Year"/>, leave this null.</summary>
    DateOnly? ReleaseDate,
    /// <summary>The looser year heuristic - always present for a <see cref="UpcomingReleaseSource.Legacy"/> row's effective date's year; a roster entry carries it independently of <see cref="ReleaseDate"/>.</summary>
    int? Year,
    long? AuthorId,
    string? AuthorName,
    long? SeriesId,
    string? SeriesName,
    string? SeriesPosition,
    string SourceName,
    string? SourceUrl,
    string? ImageUrl)
{
    /// <summary>
    /// The date this item sorts by: the precise <see cref="ReleaseDate"/> when known, else
    /// January 1st of <see cref="Year"/>, else pushed to the very end - an item with neither is
    /// not really dateable, but it is still "upcoming" and must not be dropped from the page.
    /// </summary>
    public DateOnly SortDate => ReleaseDate ?? (Year is int y ? new DateOnly(y, 1, 1) : DateOnly.MaxValue);
}
