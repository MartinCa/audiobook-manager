import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/OrphanDirectoryDto.cs: all three fields are non-nullable.
export type OrphanDirectory = Require<
  components["schemas"]["OrphanDirectoryDto"],
  "id" | "directoryPath" | "detectedAt"
>;

export type OrphanDirectoryResolveResult = Require<
  components["schemas"]["OrphanDirectoryResolveResultDto"],
  "id" | "directoryPath" | "actionTaken" | "message"
>;

export type { OrphanDirectory as default };
