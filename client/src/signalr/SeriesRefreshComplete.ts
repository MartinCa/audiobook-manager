export interface SeriesRefreshComplete {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
  stopReason: string | null;
}
