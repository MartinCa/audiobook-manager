import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/SeriesPartConflictBookDto.cs: audiobookId and bookName are
// non-nullable on the C# record; seriesPart is nullable (a book with no part is never a
// conflict, but the DTO carries the value so the UI can show it).
export type SeriesPartConflictBook = Require<
  components["schemas"]["SeriesPartConflictBookDto"],
  "audiobookId" | "bookName"
>;

// AudiobookManager.Api/Dtos/SeriesPartConflictCheckDto.cs: conflicts and truncated are both
// non-nullable on the record. truncated tells the UI the check found more conflicts than the
// bounded response carries, so it must not present the list as complete.
export type SeriesPartConflictCheck = Require<
  components["schemas"]["SeriesPartConflictCheckDto"],
  "conflicts" | "truncated"
>;

export type { SeriesPartConflictBook as default };
