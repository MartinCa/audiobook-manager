import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/AuthorSummaryDto.cs: id, name, bookCount are all non-nullable.
//
// isMatched is ALSO non-nullable on the DTO, but is deliberately left out of this Require<> list:
// AuthorDetailDto.Summary is built from a narrower source in one path (AuthorDetail.tsx's own
// summary shape) that predates the match-source fields, and AuthorsList.tsx (the only reader of
// isMatched/matchedSourceName) already treats a missing value as unmatched.
export type AuthorSummary = Require<
  components["schemas"]["AuthorSummaryDto"],
  "id" | "name" | "bookCount"
>;

export type { AuthorSummary as default };
