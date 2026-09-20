import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/BrowseFilterOptionsDto.cs: all three lists are non-nullable there.
export type BrowseFilterOptions = Require<
  components["schemas"]["BrowseFilterOptionsDto"],
  "sources" | "genres" | "languages"
>;
