import { api } from "@/lib/api";
import { PAGE_SIZE } from "@/constants/paging";
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
  SeriesBookCandidate,
  SeriesCounts,
  SeriesDetail,
  SeriesMatchCandidate,
  SeriesOverview,
  SeriesOverviewPage,
} from "@/types/Series";
import type { SeriesMapping, SeriesMappingBase, SeriesMappingGroups } from "@/types/SeriesMapping";
import type { SimilarValueGroupsPage } from "@/types/SimilarValue";
import type { SystemInfo } from "@/types/SystemInfo";
import type { TargetPathCheckResult } from "@/types/TargetPathCheck";
import type { TagMismatchField } from "@/types/TagMismatchField";
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

  searchLibrary: (q: string, limit = 5) =>
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

  getAuthorNames: () => api.get<string[]>("/similar-values/author-names"),

  getNarratorNames: () => api.get<string[]>("/similar-values/narrator-names"),

  getSeriesNames: () => api.get<string[]>("/similar-values/series-names"),

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

  getPendingForAudiobook: (id: number) =>
    api.get<PendingMetadataRefresh>(`/metadata-refresh/${id}/pending`),

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

  startRefresh: (seriesName: string) =>
    api.post<void>("/series/refresh", undefined, {
      query: { seriesName },
    }),

  startRefreshAll: () => api.post<void>("/series/refresh-all"),

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

  // Grouped server-side (the client used to reduce the flat table into buckets itself), with an
  // accent-insensitive server-side filter over pattern and target name.
  getSeriesMappingGroups: (search?: string) =>
    api.get<SeriesMappingGroups>("/settings/series_mappings/grouped", {
      query: { search: search || undefined },
    }),

  createSeriesMapping: (mapping: SeriesMappingBase) =>
    api.post<SeriesMapping>("/settings/series_mappings", mapping),

  updateSeriesMapping: (mappingId: number, mapping: SeriesMapping) =>
    api.put<SeriesMapping>(`/settings/series_mappings/${mappingId}`, mapping),

  deleteSeriesMapping: (mappingId: number) =>
    api.delete<void>(`/settings/series_mappings/${mappingId}`),

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
