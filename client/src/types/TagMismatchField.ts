import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/TagMismatchFieldDto.cs: Field is non-nullable; the two values are
// nullable (a missing tag or an empty library field serializes to null).
export type TagMismatchField = Require<components["schemas"]["TagMismatchFieldDto"], "field">;

export type TagMismatchFieldValue = {
  field: string;
  value: string | null;
};
