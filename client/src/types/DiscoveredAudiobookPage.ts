import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";
import type { DiscoveredAudiobook } from "@/types/DiscoveredAudiobook";

// AudiobookManager.Api/Dtos/DiscoveredAudiobookPageDto.cs
export type DiscoveredAudiobookPage = Require<
  components["schemas"]["DiscoveredAudiobookPageDto"],
  "count" | "total" | "wellTaggedTotal" | "items"
> & {
  items: DiscoveredAudiobook[];
};
