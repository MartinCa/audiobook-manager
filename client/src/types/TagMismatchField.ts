import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/TagMismatchFieldDto.cs: Field is non-nullable; the two values are
// nullable (a missing tag or an empty library field serializes to null).
export type TagMismatchField = Require<components["schemas"]["TagMismatchFieldDto"], "field">;

// Fields that drive the library path (AudiobookFileHandler.GenerateRelativeAudiobookPath):
// clearing one relocates the file to a mangled path and/or leaves the DB holding the old value.
// Mirrors the server's TagMismatchFields.StructuralFields — the resolve endpoint rejects a
// null/empty choice for these too, so the UI must not offer it.
export const STRUCTURAL_FIELDS = new Set(["Author", "Book Name", "Year"]);
