import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Scraping/Models/MetadataSearchServiceInfo.cs: name and enabled are
// non-nullable; disabledReason is genuinely optional.
export type MetadataSearchServiceInfo = Require<
  components["schemas"]["MetadataSearchServiceInfo"],
  "name" | "enabled"
>;
