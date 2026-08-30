import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/MissingTagDto.cs: every field on both records is non-nullable.
export type MissingTagField = Require<
  components["schemas"]["MissingTagFieldDto"],
  "key" | "label" | "isCriticalByDefault"
>;

export type AudiobookMissingTags = Require<
  components["schemas"]["AudiobookMissingTagsDto"],
  "audiobookId" | "bookName" | "authors" | "missingFields"
>;
