export interface SeriesOverview {
  id: number | null;
  name: string;
  authors: string[];
  ownedBookCount: number;
  isMatched: boolean;
  matchedSourceName: string | null;
  matchedSourceId: string | null;
  matchedSourceUrl: string | null;
  matchConfidence: number | null;
  lastRefreshedAt: string | null;
  expectedBookCount: number;
  missingBookCount: number;
  ignoredBookCount: number;
  includeOmnibusEditions: boolean;
}

export interface SeriesExpectedBook {
  id: number;
  title: string;
  position: string | null;
  year: number | null;
  sourceUrl: string | null;
  isIgnored: boolean;
}

export interface SeriesOwnedBook {
  id: number;
  bookName: string;
  seriesPart: string | null;
  year: number;
  authors: string[];
  narrators: string[];
  durationInSeconds: number | null;
}

export interface SeriesDetail {
  overview: SeriesOverview;
  ownedBooks: SeriesOwnedBook[];
  missingBooks: SeriesExpectedBook[];
  ignoredBooks: SeriesExpectedBook[];
}

export interface SeriesMatchCandidate {
  sourceName: string;
  sourceId: string;
  seriesName: string;
  sourceUrl: string | null;
  authors: string[];
  bookCount: number | null;
  confidence: number;
}
