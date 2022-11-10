import { AudiobookPerson } from "./Audiobook"

export interface BookSeriesSearchResult {
  seriesName: string,
  seriesPart?: string,
  originalSeriesName?: string,
  partWarning?: boolean
}

export interface BookSearchResult {
  url: string,
  authors: AudiobookPerson[],
  narrators: AudiobookPerson[],
  bookName: string,
  subtitle?: string,
  duration?: string,
  year: number,
  language?: string,
  imageUrl?: string,
  series: BookSeriesSearchResult[],
  description?: string,
  genres: string[],
  rating?: number,
  numberOfRatings?: number,
  copyright?: string,
  publisher?: string,
  asin?: string
}
