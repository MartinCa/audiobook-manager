/** The slice of an expected-book DTO both scopes share - the author and series expected-book rows
 * (AudiobookManager.Api/Dtos/AuthorDetailDto.cs AuthorExpectedBookDto /
 * AudiobookManager.Api/Dtos/SeriesDtos.cs SeriesExpectedBookDto) both satisfy this shape, so the
 * shared missing/upcoming/ignored list (see components/library/ExpectedBookList.tsx) can render
 * either without knowing which scope it is in.
 */
export interface ExpectedBookRow {
  id: number;
  title: string;
  isIgnored: boolean;
  position?: string | null;
  seriesName?: string | null;
  sourceSeriesName?: string | null;
  year?: number | null;
  releaseDate?: string | null;
  sourceUrl?: string | null;
}

/** Mirrors AudiobookManager.Domain.ExpectedBookClassifier.IsUpcoming (see
 * AudiobookManager/UPCOMING_RELEASES_DESIGN.md): a precise release date decides first, then the
 * bare-year heuristic. This is what splits a scope's ignored entries back into the Missing (not
 * upcoming) vs Upcoming sections when "show ignored" is on - the server keeps ignored as its own
 * list without remembering which section a row came from. The default `today` keeps the rule
 * testable; it is never rendered, only compared (a calendar-date string, never user-facing).
 */
export function isBookUpcoming(
  releaseDate: string | null | undefined,
  year: number | null | undefined,
  today: Date = utcToday(),
): boolean {
  if (releaseDate) {
    // today is anchored at UTC midnight (see utcToday), so its getUTC* parts ARE the UTC calendar
    // date the backend's DateOnly compares against - reading them with the local getters would
    // reintroduce the timezone of the anchor instant. getUTC* also keeps explicit `today` values
    // passed into this helper unambiguous.
    const isoToday = `${today.getUTCFullYear()}-${String(today.getUTCMonth() + 1).padStart(2, "0")}-${String(
      today.getUTCDate(),
    ).padStart(2, "0")}`;
    return releaseDate > isoToday;
  }
  if (year != null) {
    return year > today.getUTCFullYear();
  }
  return false;
}

/** "Today" the same way the backend computes it: the UTC calendar date
 * (DateOnly.FromDateTime(DateTime.UtcNow) in ExpectedBookClassifier), as a Date anchored at UTC
 * midnight. The default classifier argument derives from this, so client-side classification can
 * never disagree with the server at the UTC-vs-local date boundary - a release dated exactly
 * today-UTC used to be classified by the server into one section and re-classified away
 * client-side into none, dropping the row while the pager still counted it.
 */
export function utcToday(): Date {
  const now = new Date();
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()));
}
