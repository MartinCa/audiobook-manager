import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/LibrarySearchResultDto.cs: id/authors are non-nullable on
// LibraryBookHitDto; bookName and the rest are genuinely nullable.
export type LibraryBookHit = Require<components["schemas"]["LibraryBookHitDto"], "id" | "authors">;

// name and bookCount are non-nullable on both LibraryAuthorHitDto and LibrarySeriesHitDto.
export type LibraryAuthorHit = Require<
  components["schemas"]["LibraryAuthorHitDto"],
  "id" | "name" | "bookCount"
>;

export type LibrarySeriesHit = Require<
  components["schemas"]["LibrarySeriesHitDto"],
  "name" | "bookCount"
>;

export interface LibrarySearchResult {
  books: LibraryBookHit[];
  authors: LibraryAuthorHit[];
  series: LibrarySeriesHit[];
}

export type { LibrarySearchResult as default };
