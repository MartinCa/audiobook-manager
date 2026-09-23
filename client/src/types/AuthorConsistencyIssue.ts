import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/AuthorConsistencyIssueDto.cs: every field is non-nullable on the
// wire too, but the generated schema does not know that - narrowed the same way
// SeriesConsistencyIssue.ts narrows SeriesConsistencyIssueDto.
export type AuthorConsistencyIssue = Require<
  components["schemas"]["AuthorConsistencyIssueDto"],
  "id" | "personId" | "authorName" | "errorMessage" | "detectedAt"
>;

export type AuthorConsistencyIssuePage = Require<
  components["schemas"]["AuthorConsistencyIssuePageDto"],
  "items" | "totalCount"
>;
