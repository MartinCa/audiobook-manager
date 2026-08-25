import { BookSearchResult } from "./BookSearchResult";

export interface SourceSearchStatus {
  source: string;
  success: boolean;
  resultCount: number;
  error?: string;
}

export interface MultiSourceSearchResult {
  results: BookSearchResult[];
  sourceStatuses: SourceSearchStatus[];
}
