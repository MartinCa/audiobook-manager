import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/UrlCleanupDto.cs: every field on this record is non-nullable.
export type AudiobookUrlCleanup = Require<
  components["schemas"]["AudiobookUrlCleanupDto"],
  "audiobookId" | "bookName" | "authors" | "currentUrl" | "cleanedUrl"
>;

export type ApplyUrlCleanupResult = Require<
  components["schemas"]["ApplyUrlCleanupResultDto"],
  "updated"
>;
