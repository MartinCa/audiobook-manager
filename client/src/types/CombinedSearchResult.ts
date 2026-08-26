export interface BookSearchHit {
  id: number;
  bookName?: string;
  subtitle?: string;
  authors: string[];
  series?: string;
  year?: number;
  coverFilePath?: string;
}

export interface AuthorSearchHit {
  id: number;
  name: string;
  bookCount: number;
}

export interface SeriesSearchHit {
  name: string;
  bookCount: number;
}

export default interface CombinedSearchResult {
  books: BookSearchHit[];
  authors: AuthorSearchHit[];
  series: SeriesSearchHit[];
}
