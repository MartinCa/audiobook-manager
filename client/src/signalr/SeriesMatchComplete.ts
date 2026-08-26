export interface SeriesMatchComplete {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
  stopReason: string | null;
}
