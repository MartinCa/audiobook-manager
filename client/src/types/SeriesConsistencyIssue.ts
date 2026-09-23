import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/SeriesConsistencyIssueDto.cs: every field but ErrorMessage is
// non-nullable on the wire too, but the generated schema does not know that, so it is narrowed
// here the same way BookConsistencyIssue.ts narrows BookConsistencyIssueDto.
export type SeriesConsistencyIssue = Require<
  components["schemas"]["SeriesConsistencyIssueDto"],
  "id" | "seriesId" | "seriesName" | "errorMessage" | "detectedAt"
>;

export type SeriesConsistencyIssuePage = Require<
  components["schemas"]["SeriesConsistencyIssuePageDto"],
  "items" | "totalCount"
>;
