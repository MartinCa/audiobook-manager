import { z } from "zod";

export type SortDirection = "asc" | "desc";

/** The `sortBy`/`sortDir` pair the list endpoints take. Both absent means the list's own order. */
export interface ListSort {
  sortBy?: string;
  sortDir?: SortDirection;
}

export interface SortOption {
  /** The wire value of `sortBy` - the backend's camelCase sort key. */
  value: string;
  label: string;
  /** The direction picking this key starts in: counts and dates read best newest/biggest first. */
  defaultDir: SortDirection;
}

export interface SortConfig {
  options: readonly SortOption[];
  /** The key the list is in when no sort is in the URL; ascending, as the backend's default. */
  defaultKey: string;
}

// The keys mirror AuthorSortKey/SeriesSortKey/BookSortKey in the backend
// (AudiobookManager.Database/Repositories/ListSorts.cs); an unknown key is a 400 there.
export const AUTHOR_SORT: SortConfig = {
  defaultKey: "name",
  options: [
    { value: "name", label: "Name", defaultDir: "asc" },
    { value: "bookCount", label: "Number of books", defaultDir: "desc" },
    { value: "lastRefreshed", label: "Last refreshed", defaultDir: "desc" },
    { value: "missingBooks", label: "Missing books", defaultDir: "desc" },
    { value: "upcomingBooks", label: "Upcoming books", defaultDir: "desc" },
  ],
};

export const SERIES_SORT: SortConfig = {
  defaultKey: "name",
  options: [
    { value: "name", label: "Name", defaultDir: "asc" },
    { value: "bookCount", label: "Number of books", defaultDir: "desc" },
    { value: "lastRefreshed", label: "Last refreshed", defaultDir: "desc" },
    { value: "missingBooks", label: "Missing books", defaultDir: "desc" },
    { value: "completion", label: "Completion", defaultDir: "desc" },
  ],
};

export const BOOK_SORT: SortConfig = {
  defaultKey: "title",
  options: [
    { value: "title", label: "Title", defaultDir: "asc" },
    { value: "year", label: "Release year", defaultDir: "desc" },
    { value: "duration", label: "Duration", defaultDir: "desc" },
    { value: "lastRefreshed", label: "Last refreshed", defaultDir: "desc" },
    { value: "series", label: "Series", defaultDir: "asc" },
  ],
};

/**
 * The route search fields for a list's sort. A key or direction the config does not know (a
 * hand-edited or stale link) is dropped rather than failing the whole route's validation.
 */
export function sortSearchFields(config: SortConfig) {
  const keys = config.options.map((o) => o.value) as [string, ...string[]];
  return {
    sortBy: z.enum(keys).optional().catch(undefined),
    sortDir: z.enum(["asc", "desc"]).optional().catch(undefined),
  };
}

/** The sort the list is actually in: the URL's, else the config's default key ascending. */
export function effectiveSort(
  config: SortConfig,
  sort: ListSort,
): { sortBy: string; sortDir: SortDirection } {
  // A direction with no key is ignored by the backend (no sort is applied), so it must not show
  // as descending here either: the direction only counts together with an explicit key.
  if (sort.sortBy == null) return { sortBy: config.defaultKey, sortDir: "asc" };
  return { sortBy: sort.sortBy, sortDir: sort.sortDir ?? "asc" };
}

/**
 * The URL form of a chosen sort: the default key ascending is the same as no sort, so it leaves
 * the URL clean. Both fields are always present (as undefined when cleared) so a router merge
 * overwrites whatever the previous search held.
 */
export function toListSort(config: SortConfig, sortBy: string, sortDir: SortDirection): ListSort {
  if (sortBy === config.defaultKey && sortDir === "asc") {
    return { sortBy: undefined, sortDir: undefined };
  }
  return { sortBy, sortDir };
}

/** Picking another key starts in that key's natural direction. */
export function sortAfterKeyChange(config: SortConfig, sortBy: string): ListSort {
  const option = config.options.find((o) => o.value === sortBy);
  return toListSort(config, sortBy, option?.defaultDir ?? "asc");
}

export function sortAfterDirectionToggle(config: SortConfig, current: ListSort): ListSort {
  const { sortBy, sortDir } = effectiveSort(config, current);
  return toListSort(config, sortBy, sortDir === "asc" ? "desc" : "asc");
}
