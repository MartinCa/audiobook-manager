// Client-only shapes for EntityFilterBar - no 1:1 wire counterpart, mirroring how
// OrganizeAudiobookInput documents its own reason in DESIGN.md section 9: these are the filter
// query params SeriesController.GetSeries / BrowseController.GetAuthors accept, grouped for the
// shared filter bar and the two list pages' route search schemas. Date bounds are exact
// instants and travel as UTC ISO 8601 strings (the filter bar's datetime-local inputs convert
// to/from the user's own timezone); refreshedAfter is inclusive, refreshedBefore exclusive.

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
  /** Where the item sits in the review queues - see the backend's QueueState. Served options. */
  queueStates?: string[];
}

export interface SeriesListFilters extends EntityListFilters {
  minOwnedBooks?: number;
  maxOwnedBooks?: number;
}

export interface AuthorListFilters extends EntityListFilters {
  minBookCount?: number;
  maxBookCount?: number;
}

// The synthetic "no qualifier" bucket of the book "Qualifier" filter (mirrors the backend's
// BookSummaryFilter.NoQualifier). Real qualifier options are never hardcoded: they come from
// GET /settings/book-qualifiers via useBookQualifiers.
export const NO_QUALIFIER_VALUE = "(none)";

/** The book-list filters BrowseController.GetAudiobooks/SearchAudiobooks accept. */
export interface BookListFilters {
  [key: string]: boolean | number | string | string[] | undefined;
  sources?: string[];
  genres?: string[];
  languages?: string[];
  qualifiers?: string[];
  minDurationInSeconds?: number;
  maxDurationInSeconds?: number;
  // Last metadata refresh (Audiobook.LastMetadataRefreshedAt) - same instant/neverRefreshed
  // shape the series and author lists use.
  refreshedAfter?: string;
  refreshedBefore?: string;
  neverRefreshed?: boolean;
  // Where the book sits in the online-metadata review queues (a bulk search awaiting a pick or
  // rejected, a refresh awaiting review) - options come from GET /browse/filter-options.
  queueStates?: string[];
}

/** Whether any filter field is actually set - lets a caller skip sending an empty filter object. */
export function hasActiveFilters(filters: EntityListFilters): boolean {
  return Object.values(filters).some((v) => v !== undefined);
}
