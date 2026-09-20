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
    /// Derived live from a followed-and-matched series'/author's roster entry on the unified
    /// <see cref="AudiobookManager.Database.Models.ExpectedBook"/> table, classified <c>Upcoming</c> by
    /// <see cref="AudiobookManager.Domain.ExpectedBookClassifier"/>. There is no backing
    /// <c>UpcomingRelease</c> row to delete - "remove" instead sets <c>IsIgnored</c> on the shared
    /// expected-book row, the same mechanism the series/author detail pages already use to dismiss a
    /// roster entry. Addressed preferably by <see cref="UpcomingReleaseItem.ExpectedBookId"/> (the
    /// stable unified row id) or the source identity (<see cref="SourceName"/> +
    /// <see cref="SourceBookId"/>), with <see cref="UpcomingReleaseItem.SeriesName"/>/
    /// <see cref="UpcomingReleaseItem.SeriesPosition"/>/<see cref="UpcomingReleaseItem.Title"/>
    /// (series-derived) and <see cref="UpcomingReleaseItem.AuthorId"/>/
    /// <see cref="UpcomingReleaseItem.Title"/> (author-derived) as the compatibility fallback.
    /// </summary>
    Roster,
}

/// <summary>
/// One row of the unified "what's coming up for what I follow" view <see cref="IUpcomingReleaseService.GetUpcomingReleasesAsync"/>
/// returns - a union of the legacy scrape-and-store <c>UpcomingRelease</c> table and every
/// followed-and-matched series'/author's roster entries classified <c>Upcoming</c> (see
/// AudiobookManager/UPCOMING_RELEASES_DESIGN.md). <see cref="Source"/> tells the caller which
/// removal mechanism applies - see <see cref="UpcomingReleaseSource"/>.
///
/// <see cref="SourceName"/>/<see cref="SourceBookId"/> are the roster entry's source identity when
/// the item is roster-derived (the same dedup identity the author and series rosters share - one
/// unified <see cref="AudiobookManager.Database.Models.ExpectedBook"/> row, so an author-derived
/// and a series-derived item for the same book can be merged, see
/// <see cref="UpcomingReleaseService"/>) and the legacy row's source identity when legacy.
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
    /// <summary>The metadata source that reported this book (e.g. "Hardcover"), or null when unknown.</summary>
    string? SourceName,
    string? SourceUrl,
    /// <summary>A cover image URL from the source, when known - the stored unified row's image for a roster-derived item (see the expected-book <c>ImageUrl</c>), the scraped row's for a legacy one.</summary>
    string? ImageUrl,
    /// <summary>The source's own book identifier - the dedup identity shared with the roster reconciliation; null when the source gave none.</summary>
    string? SourceBookId,
    /// <summary>Internal identity for dismissal: the stable unified <see cref="AudiobookManager.Database.Models.ExpectedBook"/> row id, always set for a roster-derived item (null for a legacy one, which uses its own <see cref="Id"/> instead).</summary>
    long? ExpectedBookId = null)
{
    /// <summary>
    /// The date this item sorts by: the precise <see cref="ReleaseDate"/> when known, else
    /// January 1st of <see cref="Year"/>, else pushed to the very end - an item with neither is
    /// not really dateable, but it is still "upcoming" and must not be dropped from the page.
    /// </summary>
    public DateOnly SortDate => ReleaseDate ?? (Year is int y ? new DateOnly(y, 1, 1) : DateOnly.MaxValue);
}
