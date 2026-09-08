import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";
import type AuthorSummary from "./AuthorSummary";
import type ManagedAudiobook from "./ManagedAudiobook";

// AudiobookManager.Api/Dtos/AuthorDetailDto.cs: seriesName and bookCount are non-nullable.
export type SeriesInfo = Require<components["schemas"]["SeriesInfo"], "seriesName" | "bookCount">;

// The two sections are now paged server-side (PaginatedResult records, not plain arrays), so a
// heavy author cannot send every series and standalone book over the wire at once.
export interface AuthorDetail {
  author: AuthorSummary;
  series: {
    count: number;
    total: number;
    items: SeriesInfo[];
  };
  standaloneBooks: {
    count: number;
    total: number;
    items: ManagedAudiobook[];
  };
}

export type { AuthorDetail as default };
