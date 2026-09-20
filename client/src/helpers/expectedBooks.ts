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
  today: Date = new Date(),
): boolean {
  if (releaseDate) {
    const isoToday = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, "0")}-${String(
      today.getDate(),
    ).padStart(2, "0")}`;
    return releaseDate > isoToday;
  }
  if (year != null) {
    return year > today.getFullYear();
  }
  return false;
}
