import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/BookConsistencyIssueDto.cs: everything but ExpectedValue/ActualValue is
// non-nullable.
export type BookConsistencyIssue = Require<
  components["schemas"]["BookConsistencyIssueDto"],
  "id" | "audiobookId" | "bookName" | "authors" | "issueType" | "description" | "detectedAt"
>;

// AudiobookManager.Api/Dtos/BookConsistencyIssuePageDto.cs
export type ConsistencyIssuePage = Require<
  components["schemas"]["BookConsistencyIssuePageDto"],
  "items" | "totalCount"
>;

export type BookConsistencyResolveResult = Require<
  components["schemas"]["BookConsistencyResolveResultDto"],
  "issueId" | "issueType" | "actionTaken" | "message"
>;

export type { BookConsistencyIssue as default };
