import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/BookQualifierDtos.cs: every field on both records is non-nullable.
export type BookQualifierOption = Require<
  components["schemas"]["BookQualifierDto"],
  "key" | "label" | "suffix"
>;

export interface BookQualifierOptions {
  qualifiers: BookQualifierOption[];
}
