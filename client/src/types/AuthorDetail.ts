import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";
import type AuthorSummary from "./AuthorSummary";
import type ManagedAudiobook from "./ManagedAudiobook";
import type { SeriesOverview } from "./Series";

// AudiobookManager.Api/Dtos/AuthorDetailDto.cs (AuthorExpectedBookDto): id/title/isIgnored are
// non-nullable; year/sourceUrl/releaseDate are genuinely nullable, and the Phase 3 additions
// (position/seriesId/seriesName/sourceSeriesName/sourceName/sourceBookId/imageUrl) are all
// genuinely nullable too - a standalone book has no series, and a legacy-copied row may carry no
// source identity.
export type AuthorExpectedBook = Require<
  components["schemas"]["AuthorExpectedBookDto"],
  "id" | "title" | "isIgnored"
>;

// The two sections are now paged server-side (PaginatedResult records, not plain arrays), so a
// heavy author cannot send every series and standalone book over the wire at once. The series
// section reuses the SeriesOverview shape from the /library/series page so the author detail
// renders the same rich series entries (match badge, authors, owned/missing counts).
//
// missingBooks/upcomingBooks/ignoredBooks are the author's expected-book roster
// (AudiobookManager/UPCOMING_RELEASES_DESIGN.md) - the unified roster rows are shared with the
// series view, so series books appear in an author's roster too. Unpaged like the series detail's
// own upcoming/missing/ignored sections were before paging, but bounded by the same roster cap
// the reconciliation provider enforces, so the whole-list shape here is deliberate, not a
// regression of the bounded-list invariant.
// lastRefreshedAt is null for a never-refreshed (or unmatched) author.
export interface AuthorDetail {
  author: AuthorSummary;
  series: {
    count: number;
    total: number;
    items: SeriesOverview[];
  };
  standaloneBooks: {
    count: number;
    total: number;
    items: ManagedAudiobook[];
  };
  lastRefreshedAt: string | null;
  missingBooks: AuthorExpectedBook[];
  upcomingBooks: AuthorExpectedBook[];
  ignoredBooks: AuthorExpectedBook[];
  // The endpoint computes the missing-series groups only when asked (includeMissingSeries=true,
  // default false), so this section is null on the wire unless the caller opted in. Paged
  // server-side like the sibling sections.
  missingSeries: {
    count: number;
    total: number;
    items: AuthorMissingSeries[];
  } | null;
}

// AudiobookManager.Api/Dtos/AuthorDetailDto.cs (AuthorMissingSeriesDto): sourceName/sourceSeriesId/
// expectedCount/missingCount/upcomingCount/ownedBookCount are non-nullable on the record;
// sourceSeriesName/matchedSeriesId/matchedSeriesName are genuinely nullable (an unmatched source
// series has no matched local series).
export type AuthorMissingSeries = Require<
  components["schemas"]["AuthorMissingSeriesDto"],
  | "sourceName"
  | "sourceSeriesId"
  | "expectedCount"
  | "missingCount"
  | "upcomingCount"
  | "ownedBookCount"
>;

// AudiobookManager.Api/Dtos/AuthorDetailDto.cs: AuthorRefreshResultDto.success is non-nullable;
// lastRefreshedAt is genuinely nullable.
export type AuthorRefreshResult = Require<
  components["schemas"]["AuthorRefreshResultDto"],
  "success"
>;

export type { AuthorDetail as default };
