import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/ConsistencyIssueDto.cs: everything but ExpectedValue/ActualValue is
// non-nullable.
export type ConsistencyIssue = Require<
  components["schemas"]["ConsistencyIssueDto"],
  "id" | "audiobookId" | "bookName" | "authors" | "issueType" | "description" | "detectedAt"
>;

export type ConsistencyResolveResult = Require<
  components["schemas"]["ConsistencyResolveResultDto"],
  "issueId" | "issueType" | "actionTaken" | "message"
>;

export type { ConsistencyIssue as default };
