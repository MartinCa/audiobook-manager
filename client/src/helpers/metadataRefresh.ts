/**
 * Converts an <input type="date"> "YYYY-MM-DD" value into a UTC ISO timestamp for the bulk
 * endpoint's olderThanUtc. The backend compares LastMetadataRefreshedAt (UTC) against it, and
 * "last refreshed before this date" reads most naturally as the start of that day in the user's
 * own timezone — so local midnight, converted to UTC.
 */
export function cutoffDateToUtcIso(dateValue: string): string | undefined {
  const trimmed = dateValue.trim();
  if (!trimmed) return undefined;
  const date = new Date(`${trimmed}T00:00:00`);
  if (Number.isNaN(date.getTime())) return undefined;
  return date.toISOString();
}
