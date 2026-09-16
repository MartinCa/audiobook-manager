import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/AudiobookAuthorDto.cs: a book detail's author carrying identity for
// linking to /library/authors/{id}. id and name are non-nullable on the C# record.
export type AudiobookAuthorRef = Require<
  components["schemas"]["AudiobookAuthorDto"],
  "id" | "name"
>;

// AudiobookManager.Api/Dtos/AudiobookDetailDto.cs: authorRefs is a non-nullable List on the
// record; the additive name-only Authors list remains for existing consumers.
export type AudiobookDetail = Require<
  components["schemas"]["AudiobookDetailDto"],
  "id" | "authors" | "narrators" | "genres" | "filePath" | "fileName" | "sizeInBytes" | "authorRefs"
>;

export type { AudiobookDetail as default };
