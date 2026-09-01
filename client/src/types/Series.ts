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

// id/title/isIgnored are non-nullable; position/year/sourceUrl are genuinely nullable.
export type SeriesExpectedBook = Require<
  components["schemas"]["SeriesExpectedBookDto"],
  "id" | "title" | "isIgnored"
>;

// id/bookName/year/authors/narrators are non-nullable; seriesPart and durationInSeconds are
// genuinely nullable.
export type SeriesOwnedBook = Require<
  components["schemas"]["SeriesOwnedBookDto"],
  "id" | "bookName" | "year" | "authors" | "narrators"
>;

export interface SeriesDetail {
  overview: SeriesOverview;
  ownedBooks: SeriesOwnedBook[];
  missingBooks: SeriesExpectedBook[];
  ignoredBooks: SeriesExpectedBook[];
}

// sourceName/sourceId/seriesName/authors/confidence are non-nullable; sourceUrl and bookCount
// are genuinely nullable.
export type SeriesMatchCandidate = Require<
  components["schemas"]["SeriesMatchCandidateDto"],
  "sourceName" | "sourceId" | "seriesName" | "authors" | "confidence"
>;
