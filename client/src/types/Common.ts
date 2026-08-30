// Not generated: the backend emits a distinct concrete PaginatedResult schema per T
// (AudiobookSummaryDtoPaginatedResult, DiscoveredAudiobookDtoPaginatedResult, ...) rather than a
// reusable generic in OpenAPI. This generic wrapper is the frontend's one reusable equivalent.
export interface PaginatedResult<T> {
  count: number;
  total: number;
  items: T[];
}
