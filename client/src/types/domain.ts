export interface Audiobook {
  id: number;
  bookName: string;
  subtitle?: string;
  authors: string[];
  narrators: string[];
  series?: string;
  seriesPart?: string;
  year?: number;
  genres: string[];
  description?: string;
  copyright?: string;
  publisher?: string;
  rating?: string;
  asin?: string;
  www?: string;
  language?: string;
  durationInSeconds?: number;
  fileSizeInBytes?: number;
  fullPath: string;
  coverPath?: string;
}

export interface AudiobookDetail extends Audiobook {
  expectedPath?: string;
}

export interface DiscoveredAudiobook {
  fullPath: string;
  filename: string;
  dirPath: string;
  bookName?: string;
  authors: string[];
  narrators: string[];
  series?: string;
  seriesPart?: string;
  year?: number;
  genres: string[];
  description?: string;
  copyright?: string;
  publisher?: string;
  rating?: string;
  asin?: string;
  www?: string;
  language?: string;
  durationInSeconds?: number;
  fileSizeInBytes?: number;
}

export interface ConsistencyIssue {
  id: number;
  audiobookId: number;
  audiobookName: string;
  type: string;
  details: string;
}

export interface MissingTag {
  audiobookId: number;
  audiobookName: string;
  missingFields: string[];
}

export interface SimilarValueGroup {
  targetValue: string;
  candidates: string[];
}

export interface MetadataSearchResult {
  source: string;
  title: string;
  authors: string[];
  narrators: string[];
  series?: string;
  seriesPart?: string;
  year?: number;
  genres: string[];
  description?: string;
  coverUrl?: string;
  language?: string;
}
