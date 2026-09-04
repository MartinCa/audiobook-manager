import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/ConsistencyIssueDto.cs: everything but ExpectedValue/ActualValue is
// non-nullable.
export type ConsistencyIssue = Require<
  components["schemas"]["ConsistencyIssueDto"],
  "id" | "audiobookId" | "bookName" | "authors" | "issueType" | "description" | "detectedAt"
>;

// AudiobookManager.Api/Dtos/ConsistencyIssuePageDto.cs
export type ConsistencyIssuePage = Require<
  components["schemas"]["ConsistencyIssuePageDto"],
  "items" | "totalCount"
>;

export type ConsistencyResolveResult = Require<
  components["schemas"]["ConsistencyResolveResultDto"],
  "issueId" | "issueType" | "actionTaken" | "message"
>;

export type { ConsistencyIssue as default };
