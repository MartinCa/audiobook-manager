// Client-only shapes for EntityFilterBar - no 1:1 wire counterpart, mirroring how
// OrganizeAudiobookInput documents its own reason in DESIGN.md section 9: these are the filter
// query params SeriesController.GetSeries / BrowseController.GetAuthors accept, grouped for the
// shared filter bar and the two list pages' route search schemas. Date bounds travel as
// yyyy-MM-dd (a calendar date, matching formatHelpers' convention), not a full timestamp - the
// filter only needs day granularity.

// The synthetic "Unsupported" bucket every "Matched source" filter offers (mirrors the backend's
// AuthorSummaryFilter.UnsupportedSource/SeriesOverviewFilter.UnsupportedSource/
// BookSummaryFilter.UnsupportedSource - one constant, reused for every entry point). Relabeled to
// "Unsupported/None" on display only; the value itself stays "Unsupported" so backend filtering
// keeps working unchanged (Bug 6).
export const UNSUPPORTED_SOURCE_VALUE = "Unsupported";

/** Shared optionLabels map for every "Matched source" multiselect field (books/series/authors). */
export const SOURCE_OPTION_LABELS: Record<string, string> = {
  [UNSUPPORTED_SOURCE_VALUE]: "Unsupported/None",
};

// The index signature lets these pass directly as EntityFilterBar's generic FilterValueMap
// (the shared bar is written against string keys, not either page's specific field names).
export interface EntityListFilters {
  [key: string]: boolean | number | string | string[] | undefined;
  followed?: boolean;
  /**
   * Redundant with `sources` (selecting/excluding the "Unsupported/None" option expresses the
   * same true/false split server-side - see AuthorSummaryFilter.Matched and
   * SeriesOverviewFilter's `matched` parameter), so no filter bar exposes this as a control
   * anymore (Bug 5). Kept on the type because SeriesMatchDialog still passes `{ matched: false }`
   * directly to seriesApi.getSeriesPage to list every unmatched series for the bulk-match dialog.
   */
  matched?: boolean;
  hasMissingBooks?: boolean;
  hasUpcomingBooks?: boolean;
  refreshedAfter?: string;
  refreshedBefore?: string;
  neverRefreshed?: boolean;
  sources?: string[];
}

export interface SeriesListFilters extends EntityListFilters {
  minOwnedBooks?: number;
  maxOwnedBooks?: number;
}

export interface AuthorListFilters extends EntityListFilters {
  minBookCount?: number;
  maxBookCount?: number;
}

/** The book-list filters BrowseController.GetAudiobooks/SearchAudiobooks accept. */
export interface BookListFilters {
  [key: string]: boolean | number | string | string[] | undefined;
  sources?: string[];
  genres?: string[];
  languages?: string[];
  minDurationInSeconds?: number;
  maxDurationInSeconds?: number;
}

/** Whether any filter field is actually set - lets a caller skip sending an empty filter object. */
export function hasActiveFilters(filters: EntityListFilters): boolean {
  return Object.values(filters).some((v) => v !== undefined);
}
