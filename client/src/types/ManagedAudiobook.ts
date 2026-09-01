import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/AudiobookSummaryDto.cs: id/authors/narrators/genres are
// non-nullable. bookName, year, and the rest are genuinely nullable on the backend record.
export type ManagedAudiobook = Require<
  components["schemas"]["AudiobookSummaryDto"],
  "id" | "authors" | "narrators" | "genres"
>;

export type { ManagedAudiobook as default };
