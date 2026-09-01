import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/SimilarValueGroupDto.cs: every field on all three classes is
// non-nullable.
export type SimilarValueBook = Require<
  components["schemas"]["SimilarValueBookDto"],
  "id" | "bookName"
>;

export interface SimilarValueCandidate {
  value: string;
  bookCount: number;
  books: SimilarValueBook[];
}

export interface SimilarValueGroup {
  candidates: SimilarValueCandidate[];
}
