using AudiobookManager.Database.Models;
using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Services;

/// <summary>
/// Following authors and series for upcoming releases, matching a library author to a Hardcover
/// author (series already have their own match flow, see <see cref="ISeriesService"/>), and
/// polling followed authors/series for not-yet-released books.
/// </summary>
public interface IUpcomingReleaseService
{
    Task<bool> IsAuthorFollowedAsync(long personId);

    /// <summary>Throws <see cref="KeyNotFoundException"/> when the author does not exist.</summary>
    Task FollowAuthorAsync(long personId);

    Task UnfollowAuthorAsync(long personId);

    /// <summary>Hardcover author candidates for matching a library author, by name search.</summary>
    Task<List<AuthorSearchResult>> SearchAuthorMatchCandidatesAsync(string query);

    /// <summary>
    /// Persists the author's source match. The controller that calls this then triggers an
    /// immediate roster refresh under the shared expected-book write gate
    /// (<see cref="IExpectedBookWriteGate"/>, in <c>BrowseController.MatchAuthor</c>); the match
    /// persists even when that refresh fails transiently - the periodic sweep picks the roster up
    /// on its next tick.
    /// Throws <see cref="KeyNotFoundException"/> when the author does not exist.
    /// </summary>
    Task MatchAuthorAsync(long personId, string sourceId, string sourceName, string? sourceUrl);

    /// <summary>Clears the author's source match - their existing releases are left in place.</summary>
    Task UnmatchAuthorAsync(long personId);

    Task<bool> IsSeriesFollowedAsync(long seriesId);

    /// <summary>
    /// Followed status by series name, for the series detail page - false for a series with no
    /// catalog row at all (an unfollowed, never-matched series never has one).
    /// </summary>
    Task<bool> IsSeriesFollowedByNameAsync(string seriesName);

    /// <summary>Follows a series by name, creating its (unmatched) catalog row if needed - the
    /// same get-or-create used by the rest of the series catalog. Throws
    /// <see cref="ArgumentException"/> for a blank name.</summary>
    Task FollowSeriesAsync(string seriesName);

    Task UnfollowSeriesAsync(string seriesName);

    /// <summary>
    /// One page of upcoming releases, soonest release first, optionally scoped to one author
    /// and/or one series. No filter on either returns the consolidated view across every
    /// followed author and series. Unions the legacy scrape-and-store table with every
    /// followed-and-matched series'/author's roster entries classified <c>Upcoming</c>,
    /// de-duplicated by the roster entry's source identity - see
    /// AudiobookManager/UPCOMING_RELEASES_DESIGN.md. An author-scoped query includes that
    /// author's series books (her whole bibliography is on the unified roster) regardless of
    /// whether the series is followed; a series-scoped query only that series.
    /// </summary>
    Task<(List<UpcomingReleaseItem> Items, int Total)> GetUpcomingReleasesAsync(
        long? personId, long? seriesId, int limit, int offset);

    /// <summary>Removes one legacy upcoming release the user no longer wants tracked. Returns whether a row was deleted.</summary>
    Task<bool> RemoveUpcomingReleaseAsync(long id);

    /// <summary>
    /// Dismisses a roster-derived upcoming release for a followed author: sets <c>IsIgnored</c>
    /// on the shared unified expected-book row, the same mechanism the author detail page's
    /// missing-books section already uses. Because the row is shared, the dismissal hides the
    /// book from the series view and any other scope too (global ignore). Throws
    /// <see cref="KeyNotFoundException"/> when no entry with that title exists for the author.
    /// </summary>
    Task DismissAuthorRosterUpcomingAsync(long personId, string title);

    /// <summary>Un-dismissal counterpart of <see cref="DismissAuthorRosterUpcomingAsync"/>.</summary>
    Task RestoreAuthorRosterUpcomingAsync(long personId, string title);

    /// <summary>
    /// Dismisses a roster-derived upcoming release for a followed author's shared row addressed
    /// by the stable <see cref="AudiobookManager.Database.Models.ExpectedBook"/> row id - the
    /// unambiguous counterpart of <see cref="DismissAuthorRosterUpcomingAsync"/> (two roster
    /// entries of one author can share a title). Throws <see cref="KeyNotFoundException"/> when
    /// the id does not exist.
    /// </summary>
    Task DismissAuthorRosterUpcomingByIdAsync(long expectedBookId);

    /// <summary>Un-dismissal counterpart of <see cref="DismissAuthorRosterUpcomingByIdAsync"/>.</summary>
    Task RestoreAuthorRosterUpcomingByIdAsync(long expectedBookId);

    /// <summary>
    /// Dismisses a roster-derived upcoming release by the unified row's dedup identity - the
    /// <see cref="UpcomingReleaseItem.SourceName"/>/<see cref="UpcomingReleaseItem.SourceBookId"/>
    /// the merged upcoming view exposes, so the client can address the exact row the item came
    /// from even when it has no expected-book id. Sets <c>IsIgnored</c> on the shared row, like
    /// the id route, and invalidates any linked series' cached reconciliation. Throws
    /// <see cref="KeyNotFoundException"/> when no row carries that identity.
    /// </summary>
    Task DismissRosterUpcomingBySourceAsync(string sourceName, string sourceBookId);

    /// <summary>
    /// Dismisses a roster-derived upcoming release for a followed series: sets <c>IsIgnored</c>
    /// on the matching unified expected-book row, the same mechanism the series detail page's
    /// missing-books section already uses - globally, exactly like the author route. Throws
    /// <see cref="KeyNotFoundException"/> when no entry with that position/title exists for the
    /// series.
    /// </summary>
    Task DismissSeriesRosterUpcomingAsync(string seriesName, string? position, string title);

    /// <summary>
    /// Polls every followed-and-matched author and series for upcoming releases and stores
    /// whatever is newly discovered. Used by both the periodic worker and the manual refresh
    /// endpoint, so a user does not have to wait for the next scheduled tick to see a release
    /// that was just announced.
    /// </summary>
    Task RefreshUpcomingReleasesAsync();

    /// <summary>
    /// Refreshes one author's roster on the unified expected-books table
    /// (<see cref="ExpectedBook"/>) from their matched source: fetches the author's full
    /// bibliography - series books included, attributed to the author here while the series'
    /// own refresh stores their series placement - resolves each book's source series to the
    /// matched local catalog row when one exists, upserts the rows (ignore decisions survive
    /// automatically through the in-place upsert), prunes this author's links to exactly the
    /// fetched set, deletes the result orphans, and stamps <see cref="Person.LastRefreshedAt"/>.
    /// Throws <see cref="KeyNotFoundException"/> when the author does not exist or has no
    /// source match.
    /// </summary>
    Task RefreshAuthorRosterAsync(long personId);

    /// <summary>
    /// Refreshes the roster of every matched author, synchronously, continuing past a
    /// per-author failure (mirrors <see cref="RefreshUpcomingReleasesAsync"/>'s resilience) but
    /// stopping early if the source's daily request budget runs out. Returns how many authors
    /// were processed, how many succeeded, and - mirroring
    /// <see cref="ISeriesService.RefreshAllSeriesAsync"/>'s shape - a <c>StopReason</c> that lets
    /// the caller distinguish "stopped early because the daily request budget ran out" or "no
    /// author-capable scraper is configured" from an unremarkable "nothing to do" (no matched
    /// authors), both of which would otherwise return the same all-zero tuple.
    /// </summary>
    Task<(int Processed, int Succeeded, int Failed, string? StopReason)> RefreshAllAuthorRostersAsync();
}
