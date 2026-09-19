import { api, getOrUndefined } from "@/lib/api";
import { PAGE_SIZE, TYPEAHEAD_LIMIT } from "@/constants/paging";
import type { Audiobook } from "@/types/Audiobook";
import type { AudiobookDetail } from "@/types/AudiobookDetail";
import type { AuthorDetail } from "@/types/AuthorDetail";
import type { AuthorSummary } from "@/types/AuthorSummary";
import type { BookFileInfo } from "@/types/BookFileInfo";
import type { BulkEditAudiobooksRequest, BulkEditPreviewResponse } from "@/types/BulkEdit";
import type { PaginatedResult } from "@/types/Common";
import type {
  ConsistencyIssue,
  ConsistencyIssuePage,
  ConsistencyResolveResult,
} from "@/types/ConsistencyIssue";
import type { DiscoveredAudiobookPage } from "@/types/DiscoveredAudiobookPage";
import type { EntryStatus } from "@/types/EntryStatus";
import type { FailedOrganizeTask } from "@/types/FailedOrganizeTask";
import type { LanguageOptions } from "@/types/Language";
import type { LibrarySearchResult, LibrarySeriesHit } from "@/types/LibrarySearchResult";
import type { LibrarySettings, UpdateLibrarySettings } from "@/types/LibrarySettings";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";
import type {
  MetadataRefreshResult,
  PendingMetadataRefresh,
  PendingMetadataRefreshPage,
} from "@/types/MetadataRefresh";
import type { MetadataMultiSourceSearchResult } from "@/types/MetadataMultiSourceSearchResult";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";
import type { MetadataSearchServiceInfo } from "@/types/MetadataSearchServiceInfo";
import type { AudiobookMissingTagsPage, MissingTagField } from "@/types/MissingTag";
import type { OperationStatus } from "@/types/OperationStatus";
import type { OrphanDirectory, OrphanDirectoryResolveResult } from "@/types/OrphanDirectory";
import type {
  ApplyMissingBookSelection,
  SeriesBookCandidate,
  SeriesBulkCandidatePage,
  SeriesCounts,
  SeriesDetail,
  SeriesMatchCandidate,
  SeriesOverview,
  SeriesOverviewPage,
} from "@/types/Series";
import type {
  SeriesRefreshApplyRequest,
  SeriesRefreshPending,
  SeriesRefreshPendingPage,
  SeriesRefreshResult,
} from "@/types/SeriesRefresh";
import type { SeriesMapping, SeriesMappingBase } from "@/types/SeriesMapping";
import type { SeriesPartConflictCheck } from "@/types/SeriesPartConflict";
import type { SimilarValueGroupsPage } from "@/types/SimilarValue";
import type { SystemInfo } from "@/types/SystemInfo";
import type { TargetPathCheckResult } from "@/types/TargetPathCheck";
import type { TagMismatchField } from "@/types/TagMismatchField";
import type {
  AuthorFollowStatus,
  AuthorMatchCandidate,
  AuthorMatchStatus,
  SeriesFollowStatus,
  UpcomingRelease,
} from "@/types/UpcomingRelease";
import type { ApplyUrlCleanupResult, UrlCleanupPage } from "@/types/UrlCleanup";

export function toAudiobookDto(data: Audiobook) {
  return {
    authors: data.authors.map((a) => a.name),
    narrators: data.narrators.map((n) => n.name),
    bookName: data.bookName,
    subtitle: data.subtitle,
    series: data.series,
    seriesPart: data.seriesPart,
    year: data.year,
    genres: data.genres,
    description: data.description,
    copyright: data.copyright,
    publisher: data.publisher,
    language: data.language,
    rating: data.rating,
    asin: data.asin,
    www: data.www,
    cover: data.cover,
    filePath: data.fileInfo?.fullPath,
    fileName: data.fileInfo?.fileName,
    sizeInBytes: data.fileInfo?.sizeInBytes ?? 0,
    replaceExisting: data.replaceExisting ?? false,
    metadataAppliedFromSearch: data.metadataAppliedFromSearch ?? false,
  };
}

export function toPathPreviewDto(data: Audiobook) {
  return {
    authors: data.authors.map((a) => a.name),
    bookName: data.bookName,
    series: data.series,
    seriesPart: data.seriesPart,
    year: data.year,
    filePath: data.fileInfo?.fullPath,
    fileName: data.fileInfo?.fileName,
    sizeInBytes: data.fileInfo?.sizeInBytes ?? 0,
    replaceExisting: data.replaceExisting ?? false,
  };
}

// Untagged & Queue
export const untaggedApi = {
  getUntagged: (limit = 20, offset = 0) =>
    api.get<PaginatedResult<BookFileInfo>>("/untagged", {
      query: { limit, offset },
    }),
};

export const queueApi = {
  getQueuedBooks: () => api.get<string[]>("/queue/books"),

  getFailedTasks: () => api.get<FailedOrganizeTask[]>("/queue/failed"),

  deleteFailedTask: (originalFileLocation: string) =>
    api.delete<void>("/queue/failed", { query: { originalFileLocation } }),

  retryFailedTask: (originalFileLocation: string) =>
    api.post<void>("/queue/failed/retry", undefined, { query: { originalFileLocation } }),
};

// Audiobook Operations
export const audiobookApi = {
  parseBookDetails: (path: string) => api.post<Audiobook>("/audiobook/details", { path }),

  organizeBook: (data: Audiobook) => api.post<string>("/audiobook/organize", toAudiobookDto(data)),

  generateNewPath: (data: Audiobook) =>
    api.post<string>("/audiobook/generate_path", toPathPreviewDto(data)),

  checkTargetPath: (data: Audiobook) =>
    api.post<TargetPathCheckResult>("/audiobook/check_target_path", toPathPreviewDto(data)),

  updateBook: (id: number, data: Audiobook) =>
    api.put<void>(`/audiobook/${id}`, toAudiobookDto(data)),

  deleteAudiobook: (id: number) => api.delete<void>(`/audiobook/${id}`),

  getSaveStatus: (id: number) =>
    api.get<{ audiobookId: number; isSaving: boolean }>(`/audiobook/${id}/save-status`),

  /**
   * Advisory series-part conflict check for the edit form. Server-backed and bounded: it returns
   * the other books (current excluded) already carrying the (series, series part) combination,
   * compared with the same part-equivalence the series reconciliation uses, plus a truncation
   * flag when the bounded response does not carry all genuine conflicts. Never blocks a save.
   */
  getSeriesPartConflicts: (id: number, series: string, seriesPart: string) =>
    api.get<SeriesPartConflictCheck>(`/audiobook/${id}/series-part-conflicts`, {
      query: { series, seriesPart },
    }),
};

// Bulk selection (multi-select in the book lists)
export const bulkEditApi = {
  // Bounded by the backend's MaxBulkSelection=100; ids come from the user's explicit selection.
  preview: (audiobookIds: number[]) =>
    api.post<BulkEditPreviewResponse>("/audiobook/bulk-edit/preview", { audiobookIds }),

  apply: (audiobookIds: number[], changes: BulkEditAudiobooksRequest) =>
    api.post<void>("/audiobook/bulk-edit", { ...changes, audiobookIds }),
};

// Browse & Search
export const browseApi = {
  getAudiobooks: (limit = 20, offset = 0) =>
    api.get<PaginatedResult<ManagedAudiobook>>("/browse/audiobooks", {
      query: { limit, offset },
    }),

  searchAudiobooks: (q: string, limit = 20, offset = 0) =>
    api.get<PaginatedResult<ManagedAudiobook>>("/browse/audiobooks/search", {
      query: { q, limit, offset },
    }),

  searchLibrary: (q: string, limit = TYPEAHEAD_LIMIT) =>
    api.get<LibrarySearchResult>("/browse/library-search", {
      query: { q, limit },
    }),

  searchAuthors: (q: string, limit = 20, offset = 0) =>
    api.get<PaginatedResult<AuthorSummary>>("/browse/authors/search", {
      query: { q, limit, offset },
    }),

  searchSeries: (q: string, limit = 20, offset = 0) =>
    api.get<PaginatedResult<LibrarySeriesHit>>("/browse/series/search", {
      query: { q, limit, offset },
    }),

  // Paged server-side (bounded-list invariant): the unpaged version returned every author in the
  // library and the page rendered them all into the DOM. q is the server-side, accent-insensitive
  // filter.
  getAuthorPage: (limit: number = PAGE_SIZE, offset = 0, q?: string) =>
    api.get<PaginatedResult<AuthorSummary>>("/browse/authors", {
      query: { limit, offset, q: q || undefined },
    }),

  // The two sections are paged server-side too: an author owning hundreds of series/books used
  // to have them all sent and rendered. Pass each section's own limit/offset.
  getAuthorDetail: (
    authorId: number,
    params: {
      seriesLimit?: number;
      seriesOffset?: number;
      standaloneLimit?: number;
      standaloneOffset?: number;
    } = {},
  ) =>
    api.get<AuthorDetail>(`/browse/authors/${authorId}`, {
      query: {
        seriesLimit: params.seriesLimit,
        seriesOffset: params.seriesOffset,
        standaloneLimit: params.standaloneLimit,
        standaloneOffset: params.standaloneOffset,
      },
    }),

  getAudiobookDetail: (id: number) => api.get<AudiobookDetail>(`/browse/audiobooks/${id}`),

  getCoverUrl: (id: number) => `/api/browse/audiobooks/${id}/cover`,

  getSeriesBooks: (seriesName: string, authorId?: number) =>
    api.get<ManagedAudiobook[]>("/browse/series", {
      query: { seriesName, authorId },
    }),

  getAuthorFollowStatus: (authorId: number) =>
    api.get<AuthorFollowStatus>(`/browse/authors/${authorId}/follow`),

  followAuthor: (authorId: number) =>
    api.post<void>(`/browse/authors/${authorId}/follow`, undefined),

  unfollowAuthor: (authorId: number) => api.delete<void>(`/browse/authors/${authorId}/follow`),

  getAuthorHardcoverMatch: (authorId: number) =>
    api.get<AuthorMatchStatus>(`/browse/authors/${authorId}/hardcover-match`),

  getAuthorHardcoverMatchCandidates: (authorId: number, query?: string) =>
    api.get<AuthorMatchCandidate[]>(`/browse/authors/${authorId}/hardcover-match-candidates`, {
      query: { query: query || undefined },
    }),

  matchAuthorToHardcover: (
    authorId: number,
    sourceId: string,
    sourceName: string,
    sourceUrl?: string,
  ) =>
    api.post<void>(`/browse/authors/${authorId}/hardcover-match`, {
      sourceId,
      sourceName,
      sourceUrl,
    }),

  unmatchAuthorFromHardcover: (authorId: number) =>
    api.delete<void>(`/browse/authors/${authorId}/hardcover-match`),
};

// Library Scanning & Discovered
export const libraryApi = {
  startScan: () => api.post<void>("/library/scan"),

  getDiscovered: (limit = 20, offset = 0, search?: string) =>
    api.get<DiscoveredAudiobookPage>("/library/discovered", {
      query: { limit, offset, search: search || undefined },
    }),

  deleteDiscovered: (path: string) =>
    api.delete<void>("/library/discovered", {
      query: { path },
    }),

  bulkImport: (paths: string[]) => api.post<void>("/library/discovered/bulk-import", { paths }),

  bulkImportWellTagged: () =>
    api.post<void>("/library/discovered/bulk-import-well-tagged", undefined),
};

// Consistency
export const consistencyApi = {
  startCheck: () => api.post<void>("/consistency/check"),

  // Paged server-side. The unpaged version of this returned every issue with every field
  // inline, and ExpectedValue/ActualValue hold whole metadata.opf documents and whole
  // description bodies - megabytes of JSON to render fifty rows.
  getIssues: (params: { issueType?: string; page?: number; pageSize?: number } = {}) =>
    api.get<ConsistencyIssuePage>("/consistency/issues", {
      query: {
        issueType: params.issueType,
        page: params.page,
        pageSize: params.pageSize,
      },
    }),

  getIssueCountsByType: () => api.get<Record<string, number>>("/consistency/issues/counts-by-type"),

  /** Issue count per audiobook id, for the badges in the library list. */
  getIssueSummary: () => api.get<Record<number, number>>("/consistency/issues/summary"),

  getIssuesByAudiobook: (audiobookId: number) =>
    api.get<ConsistencyIssue[]>(`/consistency/issues/by-audiobook/${audiobookId}`),

  recheckAudiobook: (audiobookId: number) =>
    api.post<ConsistencyIssue[]>(`/consistency/issues/recheck/${audiobookId}`),

  resolveIssue: (id: number) =>
    api.post<ConsistencyResolveResult>(`/consistency/issues/${id}/resolve`),

  getTagMismatch: (id: number) =>
    api.get<TagMismatchField[]>(`/consistency/issues/${id}/tag-mismatch`),

  resolveTagMismatch: (id: number, fieldValues: Record<string, string | null>) =>
    api.post<ConsistencyResolveResult>(`/consistency/issues/${id}/tag-mismatch/resolve`, {
      fieldValues,
    }),

  // Fire-and-forget: both endpoints start a background resolve and return no result body -
  // progress and the outcome arrive over SignalR (ConsistencyResolveProgress/Complete).
  resolveSelected: (issueIds: number[]) =>
    api.post<void>("/consistency/issues/resolve-selected", issueIds),

  resolveByType: (issueType: string) =>
    api.post<void>(`/consistency/issues/resolve-by-type/${encodeURIComponent(issueType)}`),

  getOrphanDirectories: () => api.get<OrphanDirectory[]>("/consistency/orphan-directories"),

  resolveOrphanDirectory: (id: number) =>
    api.post<OrphanDirectoryResolveResult>(`/consistency/orphan-directories/${id}/resolve`),

  resolveAllOrphanDirectories: () =>
    api.post<{ resolved: number; failed: number; retained: number }>(
      "/consistency/orphan-directories/resolve-all",
    ),

  // Fire-and-forget re-check of only the explicitly selected books. Shares the full check's
  // progress/complete events, so the client has one consistency-check surface to render.
  checkSelected: (audiobookIds: number[]) =>
    api.post<void>("/consistency/check-selected", { audiobookIds }),
};

// Similar Values
export const similarValuesApi = {
  // Paged server-side: groups no longer embed per-candidate book lists (book counts only), and
  // only the requested page crosses the wire.
  getSimilarAuthors: (page = 0, pageSize: number = PAGE_SIZE) =>
    api.get<SimilarValueGroupsPage>("/similar-values/similar-authors", {
      query: { page, pageSize },
    }),

  getSimilarSeries: (page = 0, pageSize: number = PAGE_SIZE) =>
    api.get<SimilarValueGroupsPage>("/similar-values/similar-series", {
      query: { page, pageSize },
    }),

  /**
   * Bounded server-backed classification of a single typed author/narrator/series entry into
   * exact-existing / similar / new, for the entry fields' indicators. Narrator is supported
   * where the narrator entry field uses it. The bounded-list invariant is what this replaces:
   * the indicators must not pull the whole name list to classify locally.
   */
  getEntryStatus: (valueType: "author" | "narrator" | "series", value: string, limit = 3) =>
    api.get<EntryStatus>("/similar-values/entry-status", {
      query: { valueType, value, limit },
    }),

  /**
   * Bounded server-side author/narrator/series type-ahead: existing names matching the typed
   * query, accent-insensitively, capped at limit. This feeds the suggestion dropdowns of the
   * author, narrator and series entry fields - the bounded replacement for the ungated flat
   * name lists, whose size grew with the library.
   */
  getAutocomplete: (valueType: "author" | "narrator" | "series", query: string, limit: number) =>
    api.get<string[]>("/similar-values/autocomplete", {
      query: { valueType, query, limit },
    }),

  align: (valueType: "author" | "series", sourceValues: string[], targetValue: string) =>
    api.post<void>("/similar-values/align", {
      valueType,
      sourceValues,
      targetValue,
    }),
};

// Missing Tags
export const missingTagsApi = {
  getFields: () => api.get<MissingTagField[]>("/missing-tags/fields"),

  // Paged server-side (bounded-list invariant): a book missing even one selected critical tag
  // lands in this list, so the unpaged version returned thousands of rows to render into the DOM.
  getAudiobooksMissingTags: (
    fields: string[],
    params: { page?: number; pageSize?: number; search?: string } = {},
  ) =>
    api.get<AudiobookMissingTagsPage>("/missing-tags/audiobooks", {
      query: {
        fields,
        page: params.page,
        pageSize: params.pageSize,
        search: params.search,
      },
    }),

  startLanguageBackfill: () => api.post<void>("/missing-tags/backfill-language"),
};

// Metadata Refresh
export const metadataRefreshApi = {
  // Single book: synchronous (bounded by the scraper's own HTTP timeouts). All ordinary
  // per-book failures are returned in the body (Success=false + Error), not thrown.
  refreshAudiobook: (id: number) =>
    api.post<MetadataRefreshResult>(`/metadata-refresh/${id}`, undefined),

  // Fire-and-forget: starts the background bulk refresh. Progress/completion arrive over
  // SignalR (MetadataRefreshProgress/Complete) and the GET /operations/metadata-refresh/status
  // endpoint, which the page recovers from via useOperationResync.
  startBulkRefresh: (olderThanUtc?: string) =>
    api.post<void>("/metadata-refresh/bulk", {
      olderThanUtc: olderThanUtc || undefined,
    }),

  // Paged server-side (bounded-list invariant). The unpaged response would return every book
  // with a pending snapshot in one payload — the same unbounded shape UrlCleanupController
  // replaced before this list.
  getPendingPage: (page: number, pageSize: number) =>
    api.get<PendingMetadataRefreshPage>("/metadata-refresh/pending", {
      query: { page, pageSize },
    }),

  getPendingSummary: () => api.get<number[]>("/metadata-refresh/pending-summary"),

  // Single book's pending snapshot; the backend 404s when the book has none, so this resolves to
  // undefined there (the snapshot's absence is the normal state, not an error). Other failures
  // still throw.
  getPendingForAudiobook: (id: number) =>
    getOrUndefined<PendingMetadataRefresh>(`/metadata-refresh/${id}/pending`),

  dismissPending: (id: number) => api.post<void>(`/metadata-refresh/${id}/dismiss`, undefined),

  // Fire-and-forget refresh of only the explicitly selected books. Reuses the bulk-refresh
  // operation key, so a selected refresh and the all-books sweep stay mutually exclusive.
  refreshSelected: (audiobookIds: number[]) =>
    api.post<void>("/metadata-refresh/bulk-selected", { audiobookIds }),
};

// Url cleanup
export const urlCleanupApi = {
  getDirtyUrlPage: (page: number, pageSize: number) =>
    api.get<UrlCleanupPage>("/url-cleanup/audiobooks", { query: { page, pageSize } }),

  apply: (audiobookIds: number[]) =>
    api.post<ApplyUrlCleanupResult>("/url-cleanup/apply", { audiobookIds }),

  // Fire-and-forget clean of every currently-dirty book, not just the visible page. Progress and
  // completion arrive over SignalR (UrlCleanupProgress/UrlCleanupComplete); the operation status
  // is recoverable via operationsApi.getStatus(OperationKeys.urlCleanupApply).
  applyAll: () => api.post<void>("/url-cleanup/apply-all", undefined),
};

// Operations
export const operationsApi = {
  getStatus: (key: string) => api.get<OperationStatus>(`/operations/${key}/status`),
};

// Series
export const seriesApi = {
  // Paged server-side (bounded-list invariant): the union of every distinct series value in the
  // library used to be computed and returned whole. search and matched are the server-side
  // filters; the page's totals aren't the header badge counts (see getSeriesCounts).
  getSeriesPage: (page: number, pageSize: number, search?: string, matched?: boolean) =>
    api.get<SeriesOverviewPage>("/series", {
      query: { page, pageSize, search: search || undefined, matched },
    }),

  /** Total/matched/unmatched series for the overview header badges, independent of any page. */
  getSeriesCounts: () => api.get<SeriesCounts>("/series/counts"),

  getSeriesDetail: (
    seriesName: string,
    params: {
      ownedPage?: number;
      ownedPageSize?: number;
      missingPage?: number;
      missingPageSize?: number;
      ignoredPage?: number;
      ignoredPageSize?: number;
      partMismatchPage?: number;
      partMismatchPageSize?: number;
    } = {},
  ) =>
    api.get<SeriesDetail>("/series/detail", {
      query: { seriesName, ...params },
    }),

  getMatchCandidates: (seriesName: string) =>
    api.get<SeriesMatchCandidate[]>("/series/match-candidates", {
      query: { seriesName },
    }),

  searchMatchCandidates: (seriesName: string, query: string) =>
    api.get<SeriesMatchCandidate[]>("/series/match-candidates/search", {
      query: { seriesName, query },
    }),

  matchSeries: (
    seriesName: string,
    sourceName: string,
    sourceId: string,
    confidence?: number,
    includeOmnibusEditions?: boolean,
  ) =>
    api.post<SeriesOverview>(
      "/series/match",
      {
        sourceName,
        sourceId,
        confidence,
        includeOmnibusEditions,
      },
      {
        query: { seriesName },
      },
    ),

  setIncludeOmnibusEditions: (seriesName: string, includeOmnibusEditions: boolean) =>
    api.post<SeriesOverview>(
      "/series/include-omnibus-editions",
      { includeOmnibusEditions },
      {
        query: { seriesName },
      },
    ),

  startBulkMatch: (confidenceThreshold: number, seriesNames?: string[]) =>
    api.post<void>("/series/match/bulk", {
      confidenceThreshold,
      seriesNames,
    }),

  // Synchronous single-series refresh from its matched source: returns whether the source
  // differed from the library (and by how many explicit changes). A refresh that found changes
  // stores a pending snapshot for the series; a no-change refresh reports HasChanges=false.
  refreshSeries: (seriesName: string) =>
    api.post<SeriesRefreshResult>("/series/refresh", undefined, {
      query: { seriesName },
    }),

  // Fire-and-forget bulk refresh of every matched series. Progress/completion arrive over
  // SignalR (SeriesRefreshProgress/Complete); only series whose refresh found changes appear
  // in the pending list afterwards.
  startRefreshAll: () => api.post<void>("/series/refresh-all"),

  // Paged server-side (bounded-list invariant): one page of the series with a pending refresh
  // snapshot, all of whose rows exist only because a refresh found explicit changes.
  getSeriesPendingPage: (page: number, pageSize: number) =>
    api.get<SeriesRefreshPendingPage>("/series/pending", {
      query: { page, pageSize },
    }),

  getSeriesPendingCount: () => api.get<number>("/series/pending/count"),

  // The stored pending snapshot for one series; the backend 404s when none exists (a refresh that
  // found no changes leaves nothing pending), so this resolves to undefined there - the absence
  // is the normal state, not an error. Other failures still throw.
  getSeriesPending: (seriesName: string) =>
    getOrUndefined<SeriesRefreshPending>("/series/pending/detail", {
      query: { seriesName },
    }),

  dismissSeriesPending: (seriesName: string) =>
    api.post<void>("/series/pending/dismiss", undefined, {
      query: { seriesName },
    }),

  // Fire-and-forget: applies the accepted changes of a pending series refresh (and optionally
  // adopts the source's series name). Progress/completion arrive over SignalR
  // (SeriesRefreshApplyProgress/Complete) and via the GET
  // /operations/series-refresh-apply/status endpoint.
  applySeriesPending: (seriesName: string, request: SeriesRefreshApplyRequest) =>
    api.post<void>("/series/pending/apply", request, {
      query: { seriesName },
    }),

  ignoreExpectedBook: (seriesName: string, position?: string | null, title?: string | null) =>
    api.post<void>(
      "/series/expected-books/ignore",
      { position: position || undefined, title: title || undefined },
      {
        query: { seriesName },
      },
    ),

  unignoreExpectedBook: (seriesName: string, position?: string | null, title?: string | null) =>
    api.post<void>(
      "/series/expected-books/unignore",
      { position: position || undefined, title: title || undefined },
      {
        query: { seriesName },
      },
    ),

  getMissingBookCandidates: (seriesName: string, position?: string | null, title?: string | null) =>
    api.get<SeriesBookCandidate[]>("/series/expected-books/candidates", {
      query: { seriesName, position: position || undefined, title: title || undefined },
    }),

  applyMissingBook: (
    seriesName: string,
    audiobookId: number,
    position?: string | null,
    title?: string | null,
  ) =>
    api.post<void>(
      "/series/expected-books/apply",
      {
        audiobookId,
        position: position || undefined,
        title: title || undefined,
      },
      {
        query: { seriesName },
      },
    ),

  // Paged server-side (bounded-list invariant): one page of missing books with each row's ranked
  // candidate list (per-row candidates are capped server-side too).
  getBulkMissingBookCandidates: (seriesName: string, page: number, pageSize: number) =>
    api.get<SeriesBulkCandidatePage>("/series/expected-books/bulk-candidates", {
      query: { seriesName, page, pageSize },
    }),

  // Fire-and-forget: accepts only the rows the user assigned (position/title address the roster
  // entry, audiobookId the chosen library book), reports progress over SignalR.
  startBulkMissingBookApply: (seriesName: string, selections: ApplyMissingBookSelection[]) =>
    api.post<void>("/series/expected-books/apply-bulk", { selections }, { query: { seriesName } }),

  // Series mapping patterns are owned by a Series now (the target is always the owning series'
  // name, so the wire shape has no mappedSeries field and every call is scoped by seriesName in
  // the query string). These replaced the global settings-serial mapping CRUD. The list is
  // already capped server-side at the repository's per-series limit (bounded-list invariant).
  getSeriesMappings: (seriesName: string) =>
    api.get<SeriesMapping[]>("/series/mappings", {
      query: { seriesName },
    }),

  createSeriesMapping: (seriesName: string, mapping: SeriesMappingBase) =>
    api.post<SeriesMapping>("/series/mappings", mapping, {
      query: { seriesName },
    }),

  updateSeriesMapping: (seriesName: string, mappingId: number, mapping: SeriesMapping) =>
    api.put<SeriesMapping>(`/series/mappings/${mappingId}`, mapping, {
      query: { seriesName },
    }),

  deleteSeriesMapping: (seriesName: string, mappingId: number) =>
    api.delete<void>(`/series/mappings/${mappingId}`, {
      query: { seriesName },
    }),

  // Fire-and-forget: clears Series/SeriesPart on every owned book and removes the catalog row
  // (roster, mapping patterns) and any pending refresh snapshot. Progress/completion arrive over
  // SignalR (SeriesDeleteProgress/Complete) and via GET /operations/series-delete/status.
  startDeleteSeries: (seriesName: string) => api.delete<void>("/series", { query: { seriesName } }),

  getFollowStatus: (seriesName: string) =>
    api.get<SeriesFollowStatus>("/series/follow", { query: { seriesName } }),

  followSeries: (seriesName: string) =>
    api.post<void>("/series/follow", undefined, { query: { seriesName } }),

  unfollowSeries: (seriesName: string) =>
    api.delete<void>("/series/follow", { query: { seriesName } }),
};

// Upcoming Releases
export const upcomingReleasesApi = {
  // The consolidated view (no filter) and the author/series-detail-scoped views share this one
  // endpoint - authorId/seriesId narrow it server-side.
  getUpcomingReleases: (
    params: { authorId?: number; seriesId?: number; limit?: number; offset?: number } = {},
  ) =>
    api.get<PaginatedResult<UpcomingRelease>>("/upcoming-releases", {
      query: {
        authorId: params.authorId,
        seriesId: params.seriesId,
        limit: params.limit,
        offset: params.offset,
      },
    }),

  removeUpcomingRelease: (id: number) => api.delete<void>(`/upcoming-releases/${id}`),

  // Polls every followed-and-matched author/series right now rather than waiting for the
  // periodic worker's next tick.
  refreshUpcomingReleases: () => api.post<void>("/upcoming-releases/refresh", undefined),
};

// Metadata Search
export const metadataSearchApi = {
  getServices: () => api.get<MetadataSearchServiceInfo[]>("/metadata-search/services"),

  searchSource: (source: string, q: string) =>
    api.get<MetadataSearchResult[]>(`/metadata-search/${source}`, {
      query: { q },
    }),

  searchMultiple: (sources: string[], q: string) =>
    api.post<MetadataMultiSourceSearchResult>("/metadata-search/multi", {
      sources,
      q,
    }),

  getBookDetails: (path: string) =>
    api.post<MetadataSearchResult>("/metadata-search/details", { path }),

  getProxyImageUrl: (url: string) =>
    `/api/metadata-search/proxy-image?url=${encodeURIComponent(url)}`,
};

// Settings
export const settingsApi = {
  getSystemInfo: () => api.get<SystemInfo>("/settings/system_info"),

  getLanguages: () => api.get<LanguageOptions>("/settings/languages"),

  getLibrarySettings: () => api.get<LibrarySettings>("/settings/library"),

  updateLibrarySettings: (settings: UpdateLibrarySettings) =>
    api.put<LibrarySettings>("/settings/library", settings),
};

// Files
export const filesApi = {
  getDirectoryContents: (path: string) =>
    api.post<BookFileInfo[]>("/files/directory_contents", { path }),

  deleteDirectory: (path: string) => api.post<void>("/files/delete_directory", { path }),

  deleteBook: (bookPath: string) => api.post<void>("/files/delete_directory", { path: bookPath }),

  getCoverUrl: (path: string) => `/api/files/cover?path=${encodeURIComponent(path)}`,
};
