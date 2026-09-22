import type { AuthorListFilters, BookListFilters, SeriesListFilters } from "@/types/EntityFilters";

/**
 * TanStack Query key factories, one family per cached resource.
 *
 * A family's `all` (or bare) function returns the shortest prefix every variant of that family
 * shares, so `invalidateQueries({ queryKey: family.all() })` matches every paged/filtered variant
 * (TanStack Query keys are matched by structural prefix, not exact equality). Narrower functions
 * (e.g. `bySeries`, `byAuthor`) exist only where a call site intentionally invalidates a subset -
 * keep every array a literal prefix of its family's more specific arrays, in the same order, or
 * that partial-match invalidation silently stops working.
 */

export const queryKeys = {
  entryStatus: (valueType: string, value: string) => ["entryStatus", valueType, value] as const,

  languages: () => ["languages"] as const,

  // The book/author/series source filter dropdown options (registered scrapers) plus the book
  // list's genre/language filter options - see BrowseController.GetFilterOptions.
  browseFilterOptions: () => ["browseFilterOptions"] as const,

  directoryContents: (path: string) => ["directoryContents", path] as const,

  metadataRefresh: {
    all: () => ["metadataRefresh"] as const,
    pendingPage: (page: number) => ["metadataRefresh", "pending", page] as const,
    pendingForBook: (id: number) => ["metadataRefresh", "pending", id] as const,
    // No useQuery ever keys on this - getPendingSummary() is called directly - but BookDetail
    // invalidates it anyway so a future query keyed here would pick up the change for free.
    pendingSummary: () => ["metadataRefresh", "pending-summary"] as const,
  },

  librarySettings: () => ["librarySettings"] as const,

  scheduledTasks: () => ["scheduledTasks"] as const,

  systemInfo: () => ["systemInfo"] as const,

  metadataServices: () => ["metadataServices"] as const,

  consistency: {
    all: () => ["consistency"] as const,
    overview: () => ["consistency", "overview"] as const,
    page: (issueType: string, page: number) => ["consistency", "page", issueType, page] as const,
    // Issue count per audiobook id, for OwnedBookList's badges - shared across every owned-book
    // list (Bug 8 unification) rather than folded into each surface's own page query.
    issueSummary: () => ["consistency", "issueSummary"] as const,
  },

  books: {
    all: () => ["books"] as const,
    page: (q: string, page: number, pageSize: number, filters: BookListFilters = {}) =>
      ["books", q, page, pageSize, filters] as const,
  },

  tagMismatch: (issueId: number | undefined) => ["tag-mismatch", issueId] as const,

  similarValues: {
    all: () => ["similarValues"] as const,
    page: (tab: string, page: number) => ["similarValues", tab, page] as const,
    ignoredPairs: (tab: string) => ["similarValues", "ignoredPairs", tab] as const,
  },

  author: {
    all: () => ["author"] as const,
    // standaloneSearch/standaloneFilters (Bug 8 unification) are included so a filter/search
    // change on the standalone-books section refetches instead of reusing a stale cache entry.
    detail: (
      id: number,
      seriesPage: number,
      standalonePage: number,
      missingSeriesPage: number,
      standaloneSearch: string = "",
      standaloneFilters: BookListFilters = {},
    ) =>
      [
        "author",
        id,
        seriesPage,
        standalonePage,
        missingSeriesPage,
        standaloneSearch,
        standaloneFilters,
      ] as const,
  },

  // Deliberately a separate family from "author" above (the singular detail page) - a shared
  // prefix would make the list's broad invalidation also hit every open author-detail page.
  authors: {
    all: () => ["authors"] as const,
    // filters is included as a plain object - TanStack Query hashes query keys deeply, so two
    // different filter combinations (or none) never share a cache entry.
    page: (q: string, page: number, filters: AuthorListFilters) =>
      ["authors", q, page, filters] as const,
  },

  missingBookCandidates: (
    seriesName: string,
    position: string | null | undefined,
    title: string | null | undefined,
  ) => ["missingBookCandidates", seriesName, position, title] as const,

  // "seriesDetail" (this family) and "bookDetails" (below, the filesystem-path parse used by the
  // organize flow) look alike but back completely different views - do not merge them.
  seriesDetail: {
    all: () => ["seriesDetail"] as const,
    bySeries: (seriesName: string) => ["seriesDetail", seriesName] as const,
    byAuthor: (seriesName: string, authorId: number | undefined) =>
      ["seriesDetail", seriesName, authorId] as const,
    // ownedSearch/ownedFilters (Bug 8 unification) are included so a filter/search change on the
    // owned-books section refetches instead of reusing a stale cache entry.
    detail: (
      seriesName: string,
      authorId: number | undefined,
      ownedPage: number,
      missingPage: number,
      ignoredMissingPage: number,
      partMismatchPage: number,
      upcomingPage: number,
      ignoredUpcomingPage: number,
      ownedSearch: string = "",
      ownedFilters: BookListFilters = {},
    ) =>
      [
        "seriesDetail",
        seriesName,
        authorId,
        ownedPage,
        missingPage,
        ignoredMissingPage,
        partMismatchPage,
        upcomingPage,
        ignoredUpcomingPage,
        ownedSearch,
        ownedFilters,
      ] as const,
  },

  seriesCounts: () => ["seriesCounts"] as const,

  series: {
    all: () => ["series"] as const,
    page: (q: string, page: number, filters: SeriesListFilters) =>
      ["series", q, page, filters] as const,
    unmatched: (page: number) => ["series", "unmatched", page] as const,
  },

  discoveredAudiobooks: {
    all: () => ["discoveredAudiobooks"] as const,
    page: (search: string, page: number, pageSize: number) =>
      ["discoveredAudiobooks", search, page, pageSize] as const,
  },

  failedOrganizeTasks: () => ["failedOrganizeTasks"] as const,

  seriesPending: {
    all: () => ["seriesPending"] as const,
    bySeries: (seriesName: string) => ["seriesPending", seriesName] as const,
    page: (page: number) => ["seriesPending", "page", page] as const,
    count: () => ["seriesPending", "count"] as const,
  },

  bookDetail: (id: number) => ["bookDetail", id] as const,

  missingTagFields: () => ["missingTagFields"] as const,

  missingTagsAudiobooks: {
    all: () => ["missingTagsAudiobooks"] as const,
    page: (selectedFields: string[], page: number, search: string) =>
      ["missingTagsAudiobooks", selectedFields, page, search] as const,
  },

  languageBackfillStatus: () => ["languageBackfillStatus"] as const,

  seriesBulkMissingCandidates: (seriesName: string, page: number) =>
    ["seriesBulkMissingCandidates", seriesName, page] as const,

  untaggedBooks: {
    all: () => ["untaggedBooks"] as const,
    page: (page: number, pageSize: number) => ["untaggedBooks", page, pageSize] as const,
  },

  seriesPartConflicts: (bookId: number | undefined, series: string, part: string) =>
    ["seriesPartConflicts", bookId, series, part] as const,

  searchResults: {
    // filters (Bug 8 unification) defaults to {} so existing call sites that page/limit/offset
    // only (the "all" tab's preview query) keep a stable key.
    books: (
      q: string,
      page: number,
      limit: number,
      offset: number,
      filters: BookListFilters = {},
    ) => ["searchResults", "books", q, page, limit, offset, filters] as const,
    authors: (q: string, page: number, limit: number, offset: number) =>
      ["searchResults", "authors", q, page, limit, offset] as const,
    series: (q: string, page: number, limit: number, offset: number) =>
      ["searchResults", "series", q, page, limit, offset] as const,
  },

  bulkEditPreview: (idsKey: string, ids: number[]) => ["bulkEditPreview", idsKey, ids] as const,

  seriesMappings: (seriesName: string) => ["seriesMappings", seriesName] as const,

  urlCleanup: {
    all: () => ["urlCleanup"] as const,
    page: (page: number) => ["urlCleanup", "page", page] as const,
  },

  // The filesystem-path parse behind the organize flow - not the audiobook-id detail page above.
  bookDetails: (targetPath: string) => ["bookDetails", targetPath] as const,

  quickSearch: (query: string) => ["quickSearch", query] as const,

  authorFollow: (authorId: number) => ["authorFollow", authorId] as const,

  authorHardcoverMatch: (authorId: number) => ["authorHardcoverMatch", authorId] as const,

  authorHardcoverMatchCandidates: (authorId: number, query: string) =>
    ["authorHardcoverMatchCandidates", authorId, query] as const,

  seriesFollow: (seriesName: string) => ["seriesFollow", seriesName] as const,

  upcomingReleases: {
    all: () => ["upcomingReleases"] as const,
    page: (authorId: number | undefined, seriesId: number | undefined, page: number) =>
      ["upcomingReleases", authorId, seriesId, page] as const,
  },

  upcomingReleasesRefreshStatus: () => ["upcomingReleasesRefreshStatus"] as const,
} as const;
