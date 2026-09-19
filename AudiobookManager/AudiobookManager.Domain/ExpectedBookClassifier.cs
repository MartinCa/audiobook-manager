namespace AudiobookManager.Domain;

/// <summary>
/// Shared Missing-vs-Upcoming classification for a roster entry (a <c>SeriesExpectedBook</c> or
/// the author-roster equivalent) that no owned book matches. Both rosters store an optional
/// precise <c>ReleaseDate</c> alongside the looser <c>Year</c> the source has always reported -
/// see the design note at AudiobookManager/UPCOMING_RELEASES_DESIGN.md for why both exist and how
/// they classify.
///
/// The precise date is preferred whenever present; a book without one (most existing rows, until
/// their series/author is refreshed again under the new scraper fields) falls back to a
/// year-only heuristic: a book whose copyright year is still in the future is presumed unreleased
/// so far, and everything else - including a book with no year at all, which is never treated as
/// "not yet released" - is missing.
/// </summary>
public static class ExpectedBookClassifier
{
    /// <summary>
    /// Whether an unmatched roster entry should be classified as "upcoming" (not yet released)
    /// rather than "missing" (released but not owned), as of <paramref name="today"/>.
    /// </summary>
    public static bool IsUpcoming(DateOnly? releaseDate, int? year, DateOnly today)
    {
        if (releaseDate is not null)
        {
            return releaseDate.Value > today;
        }

        return year is not null && year.Value > today.Year;
    }
}
