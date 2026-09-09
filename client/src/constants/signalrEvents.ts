/**
 * Names shared with the backend over the wire. The source of truth for each set below is the
 * backend, not this file:
 *
 * - `SignalREvents` mirrors the method names of `AudiobookManager.Api.Async.IOrganize`, the
 *   SignalR hub's client-facing interface. The backend publishes every progress/completion
 *   event under exactly these names (camelCase over the wire), and the client matches them as
 *   plain strings.
 * - `OperationKeys` mirrors the operation-key constants on the API controllers (e.g.
 *   `LibraryController.ScanOperationKey`), used to poll `GET /api/operations/{key}/status`.
 *
 * `AudiobookManager.Test.Api.SignalREventParityTests` reflects over the backend and asserts
 * these sets match exactly. When the interface or the controllers grow a new event, that test
 * fails until this file is updated — and the same test fails if a name is added here that the
 * backend does not publish. Do not rename a value to match a taste: the wire name is fixed by
 * the backend.
 */

export const SignalREvents = {
  UpdateProgress: "UpdateProgress",
  QueueError: "QueueError",
  LibraryScanProgress: "LibraryScanProgress",
  LibraryScanComplete: "LibraryScanComplete",
  ConsistencyCheckProgress: "ConsistencyCheckProgress",
  ConsistencyCheckComplete: "ConsistencyCheckComplete",
  ConsistencyResolveProgress: "ConsistencyResolveProgress",
  ConsistencyResolveComplete: "ConsistencyResolveComplete",
  SimilarValueAlignProgress: "SimilarValueAlignProgress",
  SimilarValueAlignComplete: "SimilarValueAlignComplete",
  DiscoveredImportProgress: "DiscoveredImportProgress",
  DiscoveredImportComplete: "DiscoveredImportComplete",
  SeriesMatchProgress: "SeriesMatchProgress",
  SeriesMatchComplete: "SeriesMatchComplete",
  SeriesRefreshProgress: "SeriesRefreshProgress",
  SeriesRefreshComplete: "SeriesRefreshComplete",
  AudiobookSaveProgress: "AudiobookSaveProgress",
  AudiobookSaveComplete: "AudiobookSaveComplete",
  AudiobookSaveError: "AudiobookSaveError",
  MetadataRefreshProgress: "MetadataRefreshProgress",
  MetadataRefreshComplete: "MetadataRefreshComplete",
  BulkEditProgress: "BulkEditProgress",
  BulkEditComplete: "BulkEditComplete",
} as const;

export const OperationKeys = {
  consistencyCheck: "consistency-check",
  consistencyResolve: "consistency-resolve",
  consistencyCheckSelected: "consistency-check-selected",
  seriesMatch: "series-match",
  seriesRefresh: "series-refresh",
  languageBackfill: "language-backfill",
  similarValueAlign: "similar-value-align",
  metadataRefresh: "metadata-refresh",
  libraryScan: "library-scan",
  discoveredImport: "discovered-import",
  bulkEdit: "bulk-edit",
} as const;
