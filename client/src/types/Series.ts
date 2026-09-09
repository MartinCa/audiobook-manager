import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/SeriesDtos.cs: name/ownedBookCount/isMatched/expectedBookCount/
// missingBookCount/ignoredBookCount/includeOmnibusEditions are non-nullable; id and everything
// else (including the match-source fields) are genuinely nullable.
export type SeriesOverview = Require<
  components["schemas"]["SeriesOverviewDto"],
  | "name"
  | "ownedBookCount"
  | "isMatched"
  | "expectedBookCount"
  | "missingBookCount"
  | "ignoredBookCount"
  | "includeOmnibusEditions"
>;

// AudiobookManager.Api/Dtos/SeriesDtos.cs: page records carry items/totalCount non-nullable.
export type SeriesOverviewPage = Require<
  components["schemas"]["SeriesOverviewPageDto"],
  "items" | "totalCount"
> & {
  items: SeriesOverview[];
};

export type SeriesCounts = Require<
  components["schemas"]["SeriesCountsDto"],
  "total" | "matched" | "unmatched"
>;

// id/title/isIgnored are non-nullable; position/year/sourceUrl are genuinely nullable.
export type SeriesExpectedBook = Require<
  components["schemas"]["SeriesExpectedBookDto"],
  "id" | "title" | "isIgnored"
>;

export type SeriesExpectedBookPage = Require<
  components["schemas"]["SeriesExpectedBookPageDto"],
  "items" | "totalCount"
> & {
  items: SeriesExpectedBook[];
};

// id/bookName/year/authors/narrators are non-nullable; seriesPart, durationInSeconds and
// coverFilePath are genuinely nullable.
export type SeriesOwnedBook = Require<
  components["schemas"]["SeriesOwnedBookDto"],
  "id" | "bookName" | "year" | "authors" | "narrators"
>;

export type SeriesOwnedBookPage = Require<
  components["schemas"]["SeriesOwnedBookPageDto"],
  "items" | "totalCount"
> & {
  items: SeriesOwnedBook[];
};

export interface SeriesDetail {
  overview: SeriesOverview;
  ownedBooks: SeriesOwnedBookPage;
  missingBooks: SeriesExpectedBookPage;
  ignoredBooks: SeriesExpectedBookPage;
}

// sourceName/sourceId/seriesName/authors/confidence are non-nullable; sourceUrl and bookCount
// are genuinely nullable.
export type SeriesMatchCandidate = Require<
  components["schemas"]["SeriesMatchCandidateDto"],
  "sourceName" | "sourceId" | "seriesName" | "authors" | "confidence"
>;

// AudiobookManager.Api/Dtos/SeriesDtos.cs: audiobookId/bookName/year/authors/titleSimilarity/
// authorMatches are non-nullable; series and seriesPart are genuinely nullable.
export type SeriesBookCandidate = Require<
  components["schemas"]["SeriesBookCandidateDto"],
  "audiobookId" | "bookName" | "year" | "authors" | "titleSimilarity" | "authorMatches"
>;
