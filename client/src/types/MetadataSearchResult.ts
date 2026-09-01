import { type AudiobookPerson } from "./Audiobook";

export interface MetadataSeriesSearchResult {
  seriesName: string;
  seriesPart?: string;
  originalSeriesName?: string;
  partWarning?: boolean;
}

export interface MetadataSearchResult {
  url: string;
  source: string;
  authors: AudiobookPerson[];
  narrators: AudiobookPerson[];
  bookName: string;
  subtitle?: string;
  duration?: string;
  year: number;
  language?: string;
  imageUrl?: string;
  series: MetadataSeriesSearchResult[];
  description?: string;
  genres: string[];
  rating?: number;
  numberOfRatings?: number;
  copyright?: string;
  publisher?: string;
  asin?: string;
  isbn?: string;
}
