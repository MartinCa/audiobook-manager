import { api } from "@/lib/api";
import type { Audiobook } from "@/types/Audiobook";
import type { AudiobookDetail } from "@/types/AudiobookDetail";
import type { AuthorDetail } from "@/types/AuthorDetail";
import type { AuthorSummary } from "@/types/AuthorSummary";
import type { BookFileInfo } from "@/types/BookFileInfo";
import type { PaginatedResult } from "@/types/Common";
import type { ConsistencyIssue, ConsistencyResolveResult } from "@/types/ConsistencyIssue";
import type { DiscoveredAudiobook } from "@/types/DiscoveredAudiobook";
import type { LanguageOptions } from "@/types/Language";
import type { LibrarySearchResult } from "@/types/LibrarySearchResult";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";
import type { MetadataMultiSourceSearchResult } from "@/types/MetadataMultiSourceSearchResult";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";
import type { MetadataSearchServiceInfo } from "@/types/MetadataSearchServiceInfo";
import type { AudiobookMissingTags, MissingTagField } from "@/types/MissingTag";
import type { OperationStatus } from "@/types/OperationStatus";
import type { OrphanDirectory, OrphanDirectoryResolveResult } from "@/types/OrphanDirectory";
import type { SeriesDetail, SeriesMatchCandidate, SeriesOverview } from "@/types/Series";
import type { SeriesMapping, SeriesMappingBase } from "@/types/SeriesMapping";
import type { SimilarValueGroup } from "@/types/SimilarValue";
import type { SystemInfo } from "@/types/SystemInfo";
import type { TargetPathCheckResult } from "@/types/TargetPathCheck";

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

  getAuthors: () => api.get<AuthorSummary[]>("/browse/authors"),

  getAuthorDetail: (authorId: number) => api.get<AuthorDetail>(`/browse/authors/${authorId}`),

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
    api.get<PaginatedResult<DiscoveredAudiobook>>("/library/discovered", {
      query: { limit, offset, search: search || undefined },
    }),

  deleteDiscovered: (path: string) =>
    api.delete<void>("/library/discovered", {
      query: { path },
    }),

  bulkImport: (paths: string[]) => api.post<void>("/library/discovered/bulk-import", { paths }),
};

// Consistency
export const consistencyApi = {
  startCheck: () => api.post<void>("/consistency/check"),

  getIssues: () => api.get<ConsistencyIssue[]>("/consistency/issues"),

  getIssuesByAudiobook: (audiobookId: number) =>
    api.get<ConsistencyIssue[]>(`/consistency/issues/by-audiobook/${audiobookId}`),

  recheckAudiobook: (audiobookId: number) =>
    api.post<ConsistencyIssue[]>(`/consistency/issues/recheck/${audiobookId}`),

  resolveIssue: (id: number) =>
    api.post<ConsistencyResolveResult>(`/consistency/issues/${id}/resolve`),

  resolveSelected: (issueIds: number[]) =>
    api.post<{ resolved: number; failed: number }>(
      "/consistency/issues/resolve-selected",
      issueIds,
    ),

  resolveByType: (issueType: string) =>
    api.post<{ resolved: number; failed: number }>(
      `/consistency/issues/resolve-by-type/${encodeURIComponent(issueType)}`,
    ),

  getOrphanDirectories: () => api.get<OrphanDirectory[]>("/consistency/orphan-directories"),

  resolveOrphanDirectory: (id: number) =>
    api.post<OrphanDirectoryResolveResult>(`/consistency/orphan-directories/${id}/resolve`),

  resolveAllOrphanDirectories: () =>
    api.post<{ resolved: number; failed: number; retained: number }>(
      "/consistency/orphan-directories/resolve-all",
    ),
};

// Similar Values
export const similarValuesApi = {
  getSimilarAuthors: () => api.get<SimilarValueGroup[]>("/similar-values/similar-authors"),

  getSimilarSeries: () => api.get<SimilarValueGroup[]>("/similar-values/similar-series"),

  getAuthorNames: () => api.get<string[]>("/similar-values/author-names"),

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

  getAudiobooksMissingTags: (fields: string[]) =>
    api.get<AudiobookMissingTags[]>("/missing-tags/audiobooks", {
      query: { fields },
    }),

  startLanguageBackfill: () => api.post<void>("/missing-tags/backfill-language"),
};

// Operations
export const operationsApi = {
  getStatus: (key: string) => api.get<OperationStatus>(`/operations/${key}/status`),
};

// Series
export const seriesApi = {
  getAllSeries: () => api.get<SeriesOverview[]>("/series"),

  getSeriesDetail: (seriesName: string) =>
    api.get<SeriesDetail>("/series/detail", {
      query: { seriesName },
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

  getSeriesMappings: () => api.get<SeriesMapping[]>("/settings/series_mappings"),

  createSeriesMapping: (mapping: SeriesMappingBase) =>
    api.post<SeriesMapping>("/settings/series_mappings", mapping),

  updateSeriesMapping: (mappingId: number, mapping: SeriesMapping) =>
    api.put<SeriesMapping>(`/settings/series_mappings/${mappingId}`, mapping),

  deleteSeriesMapping: (mappingId: number) =>
    api.delete<void>(`/settings/series_mappings/${mappingId}`),
};

// Files
export const filesApi = {
  getDirectoryContents: (path: string) =>
    api.post<BookFileInfo[]>("/files/directory_contents", { path }),

  deleteDirectory: (path: string) => api.post<void>("/files/delete_directory", { path }),

  deleteBook: (bookPath: string) => api.post<void>("/files/delete_directory", { path: bookPath }),

  getCoverUrl: (path: string) => `/api/files/cover?path=${encodeURIComponent(path)}`,
};
