import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/DiscoveredAudiobookDto.cs: a flat shape (no nested fileInfo) with
// authors/narrators/genres as "/"-joined strings, not arrays — same convention as the organize
// form fields (see helpers/organizeAudiobookInput.ts's splitList/joinList). fullPath, fileName,
// sizeInBytes, bookName, isWellTagged, and isDuplicate are non-nullable on the backend record;
// everything else (including year) is genuinely nullable.
export type DiscoveredAudiobook = Require<
  components["schemas"]["DiscoveredAudiobookDto"],
  "fullPath" | "fileName" | "sizeInBytes" | "bookName" | "isWellTagged" | "isDuplicate"
>;

export type { DiscoveredAudiobook as default };
