import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/FailedOrganizeTaskDto.cs: originalFileLocation/queuedTime/failureCount
// are non-nullable; lastFailureReason/lastFailureAt are genuinely nullable (a row that has never
// failed - not something this endpoint returns - would have neither set).
export type FailedOrganizeTask = Require<
  components["schemas"]["FailedOrganizeTaskDto"],
  "originalFileLocation" | "queuedTime" | "failureCount"
>;

export type { FailedOrganizeTask as default };
