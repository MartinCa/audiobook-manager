import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// The two allowed values, as a discriminated-choice literal the select component keys off.
// Mirrors Domain.InitialsSpacing / Database.Models.InitialsSpacing.
export type InitialsSpacing = "Spaced" | "Unspaced";

// Mirrors Domain.InitialsPunctuation / Database.Models.InitialsPunctuation.
export type InitialsPunctuation = "Dotted" | "Undotted";

// Response shape of GET api/settings/library. The backend emits the enums as their name strings
// ("Spaced"/"Unspaced", "Dotted"/"Undotted"), and PUT refusal is the only path a different value
// could ever arrive by, so the wire strings are narrowed to the literals the selects can represent.
// Require<> keys checked against AudiobookManager.Api.Dtos.LibrarySettingsDto - every field is a
// plain non-nullable record positional property there.
export type LibrarySettings = Require<
  components["schemas"]["LibrarySettingsDto"],
  | "initialsSpacing"
  | "initialsPunctuation"
  | "metadataRefreshDelayMs"
  | "upcomingReleasesEnabled"
  | "upcomingReleasesCronSchedule"
> & { initialsSpacing: InitialsSpacing; initialsPunctuation: InitialsPunctuation };

// Body of PUT api/settings/library. initialsPunctuation/metadataRefreshDelayMs/
// upcomingReleasesEnabled/upcomingReleasesCronSchedule are optional on the wire too - an omitted
// field keeps the stored value rather than resetting it (see
// SettingsController.UpdateLibrarySettings).
export type UpdateLibrarySettings = {
  initialsSpacing: InitialsSpacing;
  initialsPunctuation?: InitialsPunctuation;
  metadataRefreshDelayMs?: number;
  upcomingReleasesEnabled?: boolean;
  upcomingReleasesCronSchedule?: string;
};
