import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/OrganizeAudiobookDto.cs: sizeInBytes is non-nullable; audiobookId
// and durationInSeconds are genuinely nullable.
export type ExistingTargetFile = Require<
  components["schemas"]["ExistingTargetFileDto"],
  "sizeInBytes"
>;

// targetPath and exists are non-nullable; existing is only present when exists is true.
export interface TargetPathCheckResult {
  targetPath: string;
  exists: boolean;
  existing?: ExistingTargetFile;
}
