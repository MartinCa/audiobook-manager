import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/SimilarValueGroupDto.cs: candidates carry a value and a book count —
// not the per-candidate book lists the unpaged response embedded, which is what made the
// detection payload grow with the library. All fields are non-nullable.
export interface SimilarValueCandidate {
  value: string;
  bookCount: number;
}

export interface SimilarValueGroup {
  candidates: SimilarValueCandidate[];
}

export type SimilarValueGroupsPage = Require<
  components["schemas"]["SimilarValueGroupsPageDto"],
  "items" | "totalCount"
> & {
  items: SimilarValueGroup[];
};
