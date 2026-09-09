import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/BulkEditDtos.cs: BulkEditPreviewItemDto is a positional record whose
// id, authors, narrators, and genres are non-nullable; every other field is genuinely nullable
// on the backend record.
export type BulkEditPreviewBook = Require<
  components["schemas"]["BulkEditPreviewItemDto"],
  "id" | "authors" | "narrators" | "genres"
>;

// AudiobookManager.Api/Dtos/BulkEditDtos.cs: Books is initialized to a new list, so it is never
// null in a successful response.
export type BulkEditPreviewResponse = Require<
  components["schemas"]["BulkEditPreviewResponseDto"],
  "books"
> & {
  books: BulkEditPreviewBook[];
};

// AudiobookManager.Api/Dtos/BulkEditDtos.cs: audiobookIds carries [Required]; every field-edit
// object is optional by design (a field with no change object is left untouched).
export type BulkEditAudiobooksRequest = components["schemas"]["BulkEditAudiobooksRequestDto"];

export type { BulkEditPreviewBook as default };
