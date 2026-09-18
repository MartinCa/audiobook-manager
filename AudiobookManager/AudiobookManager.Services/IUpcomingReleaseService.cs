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
    /// Sets the author's Hardcover match, so the poll can fetch their upcoming releases.
    /// Throws <see cref="KeyNotFoundException"/> when the author does not exist.
    /// </summary>
    Task MatchAuthorAsync(long personId, string sourceId, string sourceName, string? sourceUrl);

    /// <summary>Clears the author's Hardcover match - their existing releases are left in place.</summary>
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
    /// followed author and series.
    /// </summary>
    Task<(List<UpcomingRelease> Items, int Total)> GetUpcomingReleasesAsync(
        long? personId, long? seriesId, int limit, int offset);

    /// <summary>Removes one upcoming release the user no longer wants tracked. Returns whether a row was deleted.</summary>
    Task<bool> RemoveUpcomingReleaseAsync(long id);

    /// <summary>
    /// Polls every followed-and-matched author and series for upcoming releases and stores
    /// whatever is newly discovered. Used by both the periodic worker and the manual refresh
    /// endpoint, so a user does not have to wait for the next scheduled tick to see a release
    /// that was just announced.
    /// </summary>
    Task RefreshUpcomingReleasesAsync();
}
