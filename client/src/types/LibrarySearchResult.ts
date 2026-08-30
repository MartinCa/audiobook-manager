export interface LibraryBookHit {
  id: number;
  bookName: string;
  subtitle?: string;
  authors: string[];
  series?: string;
  year?: number;
  coverFilePath?: string;
}

export interface LibraryAuthorHit {
  id: number;
  name: string;
  bookCount: number;
}

export interface LibrarySeriesHit {
  name: string;
  bookCount: number;
}

export interface LibrarySearchResult {
  books: LibraryBookHit[];
  authors: LibraryAuthorHit[];
  series: LibrarySeriesHit[];
}

export type { LibrarySearchResult as default };
