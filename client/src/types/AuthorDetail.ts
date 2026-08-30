import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";
import type AuthorSummary from "./AuthorSummary";
import type ManagedAudiobook from "./ManagedAudiobook";

// AudiobookManager.Api/Dtos/AuthorDetailDto.cs: seriesName and bookCount are non-nullable.
export type SeriesInfo = Require<components["schemas"]["SeriesInfo"], "seriesName" | "bookCount">;

// author/series/standaloneBooks are all non-nullable on the record; the nested shapes reuse the
// AuthorSummary/ManagedAudiobook aliases rather than the raw (unnarrowed) generated element type.
export interface AuthorDetail {
  author: AuthorSummary;
  series: SeriesInfo[];
  standaloneBooks: ManagedAudiobook[];
}

export type { AuthorDetail as default };
