import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// The two allowed values, as a discriminated-choice literal the select component keys off.
// Mirrors Domain.InitialsSpacing / Database.Models.InitialsSpacing.
export type InitialsSpacing = "Spaced" | "Unspaced";

// Response shape of GET api/settings/library. The backend emits the enum as its name string
// ("Spaced"/"Unspaced"), and PUT refusal is the only path a different value could ever arrive
// by, so the wire string is narrowed to the two literals the select can represent.
export type LibrarySettings = Require<
  components["schemas"]["LibrarySettingsDto"],
  "initialsSpacing"
> & { initialsSpacing: InitialsSpacing };

// Body of PUT api/settings/library.
export type UpdateLibrarySettings = {
  initialsSpacing: InitialsSpacing;
};
