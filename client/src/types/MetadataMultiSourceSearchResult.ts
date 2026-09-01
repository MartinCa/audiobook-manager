import { type MetadataSearchResult } from "./MetadataSearchResult";

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
