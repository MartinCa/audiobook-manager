import type AuthorSummary from "./AuthorSummary";
import type ManagedAudiobook from "./ManagedAudiobook";
import type { SeriesOverview } from "./Series";

// The two sections are now paged server-side (PaginatedResult records, not plain arrays), so a
// heavy author cannot send every series and standalone book over the wire at once. The series
// section reuses the SeriesOverview shape from the /library/series page so the author detail
// renders the same rich series entries (match badge, authors, owned/missing counts).
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
}

export type { AuthorDetail as default };
