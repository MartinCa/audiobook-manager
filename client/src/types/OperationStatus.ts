import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/OperationStatusDto.cs: all three fields are non-nullable.
export type OperationStatus = Require<
  components["schemas"]["OperationStatusDto"],
  "isRunning" | "processed" | "total"
>;
