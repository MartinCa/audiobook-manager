import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// The two allowed values, as a discriminated-choice literal the select component keys off.
// Mirrors Domain.InitialsSpacing / Database.Models.InitialsSpacing.
export type InitialsSpacing = "Spaced" | "Unspaced";

// Mirrors Domain.InitialsPunctuation / Database.Models.InitialsPunctuation.
export type InitialsPunctuation = "Dotted" | "Undotted";

// Mirrors Domain.SearchInitialsHandling / Database.Models.SearchInitialsHandling: how author
// initials are written in the default online-metadata search query.
export type SearchInitialsHandling = "AsStored" | "Compact" | "Spaced";

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
  | "defaultPageSize"
  | "searchInitialsHandling"
  | "includeNarratorInPath"
  | "maxNarratorsInPath"
  | "autoSplitTitleOnColon"
  | "mainAuthorOnlyInPath"
  | "searchInitialsHandlingBySource"
> & {
  initialsSpacing: InitialsSpacing;
  initialsPunctuation: InitialsPunctuation;
  searchInitialsHandling: SearchInitialsHandling;
  /**
   * Per-source overrides of `searchInitialsHandling` for the default and automated search query,
   * keyed by source name. A source with no entry follows the library-wide setting. The values are
   * the same wire strings as `searchInitialsHandling`.
   */
  searchInitialsHandlingBySource: Record<string, SearchInitialsHandling>;
};

// Body of PUT api/settings/library. initialsPunctuation/metadataRefreshDelayMs/
// upcomingReleasesEnabled/upcomingReleasesCronSchedule/defaultPageSize/searchInitialsHandling/
// includeNarratorInPath/maxNarratorsInPath/autoSplitTitleOnColon/mainAuthorOnlyInPath are optional
// on the wire too - an omitted field keeps the stored value rather than resetting it (see
// SettingsController.UpdateLibrarySettings). searchInitialsHandlingBySource, when sent, replaces
// the whole set of per-source overrides (an empty object clears them).
export type UpdateLibrarySettings = {
  initialsSpacing: InitialsSpacing;
  initialsPunctuation?: InitialsPunctuation;
  metadataRefreshDelayMs?: number;
  upcomingReleasesEnabled?: boolean;
  upcomingReleasesCronSchedule?: string;
  defaultPageSize?: number;
  searchInitialsHandling?: SearchInitialsHandling;
  includeNarratorInPath?: boolean;
  maxNarratorsInPath?: number;
  autoSplitTitleOnColon?: boolean;
  mainAuthorOnlyInPath?: boolean;
  searchInitialsHandlingBySource?: Record<string, SearchInitialsHandling>;
};
