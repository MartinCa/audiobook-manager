import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/AuthorSummaryDto.cs: id, name, bookCount are all non-nullable.
export type AuthorSummary = Require<
  components["schemas"]["AuthorSummaryDto"],
  "id" | "name" | "bookCount"
>;

export type { AuthorSummary as default };
