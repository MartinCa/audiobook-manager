import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";
import type AuthorSummary from "./AuthorSummary";
import type ManagedAudiobook from "./ManagedAudiobook";
import type { SeriesOverview } from "./Series";

// AudiobookManager.Api/Dtos/AuthorDetailDto.cs (AuthorExpectedBookDto): id/title/isIgnored are
// non-nullable; year/sourceUrl/releaseDate are genuinely nullable - mirrors SeriesExpectedBook.
export type AuthorExpectedBook = Require<
  components["schemas"]["AuthorExpectedBookDto"],
  "id" | "title" | "isIgnored"
>;

// The two sections are now paged server-side (PaginatedResult records, not plain arrays), so a
// heavy author cannot send every series and standalone book over the wire at once. The series
// section reuses the SeriesOverview shape from the /library/series page so the author detail
// renders the same rich series entries (match badge, authors, owned/missing counts).
//
// missingBooks/upcomingBooks are the author's standalone-books roster (AudiobookManager/
// UPCOMING_RELEASES_DESIGN.md) - unpaged like the series detail's own upcoming/missing sections
// were before paging, but bounded by the same roster cap the reconciliation provider enforces, so
// the whole-list shape here is deliberate, not a regression of the bounded-list invariant.
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
}

// AudiobookManager.Api/Dtos/AuthorDetailDto.cs: AuthorRefreshResultDto.success is non-nullable;
// lastRefreshedAt is genuinely nullable. AuthorRefreshAllResultDto's three counters are all
// non-nullable.
export type AuthorRefreshResult = Require<
  components["schemas"]["AuthorRefreshResultDto"],
  "success"
>;

export type AuthorRefreshAllResult = Require<
  components["schemas"]["AuthorRefreshAllResultDto"],
  "processed" | "succeeded" | "failed"
>;

export type { AuthorDetail as default };
