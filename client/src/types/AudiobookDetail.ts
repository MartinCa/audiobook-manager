import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/AudiobookDetailDto.cs: id/authors/narrators/genres/filePath/
// fileName/sizeInBytes are non-nullable. bookName and year are genuinely nullable on the
// backend record — do not assume either is always present.
export type AudiobookDetail = Require<
  components["schemas"]["AudiobookDetailDto"],
  "id" | "authors" | "narrators" | "genres" | "filePath" | "fileName" | "sizeInBytes"
>;

export type { AudiobookDetail as default };
