import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/SeriesDtos.cs: name/ownedBookCount/isMatched/expectedBookCount/
// missingBookCount/ignoredBookCount/includeOmnibusEditions/upcomingBookCount are non-nullable;
// id and everything else (including the match-source fields) are genuinely nullable.
export type SeriesOverview = Require<
  components["schemas"]["SeriesOverviewDto"],
  | "name"
  | "ownedBookCount"
  | "isMatched"
  | "expectedBookCount"
  | "missingBookCount"
  | "ignoredBookCount"
  | "includeOmnibusEditions"
  | "upcomingBookCount"
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

// id/title/isIgnored are non-nullable; position/year/sourceUrl/releaseDate are genuinely
// nullable.
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

// AudiobookManager.Api/Dtos/SeriesDtos.cs (SeriesPartMismatchDto): audiobookId/bookName/
// expectedPart/rosterTitle are non-nullable; storedPart is genuinely nullable (a book with no
// part at all is exactly the missing-part shape this is used to surface).
export type SeriesPartMismatch = Require<
  components["schemas"]["SeriesPartMismatchDto"],
  "audiobookId" | "bookName" | "expectedPart" | "rosterTitle"
>;

export type SeriesPartMismatchPage = Require<
  components["schemas"]["SeriesPartMismatchPageDto"],
  "items" | "totalCount"
> & {
  items: SeriesPartMismatch[];
};

export interface SeriesDetail {
  overview: SeriesOverview;
  ownedBooks: SeriesOwnedBookPage;
  missingBooks: SeriesExpectedBookPage;
  ignoredBooks: SeriesExpectedBookPage;
  partMismatches: SeriesPartMismatchPage;
  upcomingBooks: SeriesExpectedBookPage;
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

// AudiobookManager.Api/Dtos/SeriesDtos.cs (SeriesBulkCandidateItemDto): book and candidates are
// non-nullable on the record (candidates may be an empty list); the nullable forms on the wire
// come from there being no [Required] on a record's positional properties. The Omit strips the
// generated optional/nullable keys before the narrowed shapes are put back, so iterating
// candidates yields SeriesBookCandidate - not a bare intersection that keeps the optional fields.
export type SeriesBulkCandidateItem = Omit<
  components["schemas"]["SeriesBulkCandidateItemDto"],
  "book" | "candidates"
> & {
  book: SeriesExpectedBook;
  candidates: SeriesBookCandidate[];
};

export type SeriesBulkCandidatePage = Omit<
  components["schemas"]["SeriesBulkCandidatePageDto"],
  "items" | "totalCount"
> & {
  items: SeriesBulkCandidateItem[];
  totalCount: number;
};

// The wire shape of one accepted assignment in a bulk missing-book apply (ApplyMissingBookBulkRequestDto
// with [Required] on selections/audiobookId, unlike the response records).
export type ApplyMissingBookSelection = components["schemas"]["ApplyMissingBookSelectionDto"];
export type ApplyMissingBookBulkRequest = components["schemas"]["ApplyMissingBookBulkRequestDto"];
