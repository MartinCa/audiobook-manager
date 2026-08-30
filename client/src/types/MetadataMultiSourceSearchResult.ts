import type { MetadataSearchResult } from "./MetadataSearchResult";

// AudiobookManager.Scraping/Models/MetadataMultiSourceSearchResult.cs: both fields default to a
// non-null empty list and are never set to null.
export interface MetadataSourceSearchStatus {
  source: string;
  success: boolean;
  resultCount: number;
  error?: string;
}

export interface MetadataMultiSourceSearchResult {
  results: MetadataSearchResult[];
  sourceStatuses: MetadataSourceSearchStatus[];
}

export type { MetadataMultiSourceSearchResult as default };
