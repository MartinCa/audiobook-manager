import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/MissingTagDto.cs: MissingTagFieldDto's fields are all non-nullable.
// AudiobookMissingTagsDto mirrors AudiobookSummaryDto's shape (see ManagedAudiobook) plus
// MissingFields; audiobookId/bookName/authors/narrators/missingFields are non-nullable there,
// the rest (year/series/seriesPart/coverFilePath/durationInSeconds/isMatched/matchedSourceName)
// genuinely nullable/absent for an untagged book.
export type MissingTagField = Require<
  components["schemas"]["MissingTagFieldDto"],
  "key" | "label" | "isCriticalByDefault"
>;

export type AudiobookMissingTags = Require<
  components["schemas"]["AudiobookMissingTagsDto"],
  "audiobookId" | "bookName" | "authors" | "narrators" | "missingFields"
>;

export type AudiobookMissingTagsPage = Require<
  components["schemas"]["AudiobookMissingTagsPageDto"],
  "items" | "totalCount"
> & {
  items: AudiobookMissingTags[];
};
