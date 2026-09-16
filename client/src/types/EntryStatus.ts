import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/EntryStatusDtos.cs: value, status and similarMatches are non-nullable
// on the record (Status is a string in C# too: "exact" | "similar" | "new"). exactMatch is a
// nullable record property - it is only set for the "exact" classification, and the OpenAPI
// schema renders it optional, so its type is restored to the C# nullability here.
export type EntryStatus = Omit<
  Require<components["schemas"]["EntryStatusDto"], "value" | "status">,
  "exactMatch" | "similarMatches"
> & {
  exactMatch: EntryMatch | null;
  similarMatches: EntryMatch[];
};

// id is null for series matches (free text, no identity) and set for author matches - the C#
// record leaves it nullable, so it stays nullable/optional here; only name is restored.
export type EntryMatch = Omit<components["schemas"]["EntryMatchDto"], "name"> & { name: string };

export type { EntryStatus as default };
