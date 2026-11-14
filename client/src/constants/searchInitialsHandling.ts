import type { SearchInitialsHandling } from "@/types/LibrarySettings";

/**
 * The ways author initials can be written in the default online-metadata search query, with the
 * wording the settings page shows. The same list backs the library-wide select and each source's
 * own select, so they cannot drift. The values mirror `Domain.SearchInitialsHandling`.
 */
export const SEARCH_INITIALS_HANDLING_OPTIONS: { value: SearchInitialsHandling; label: string }[] =
  [
    { value: "AsStored", label: "As stored (the name as it is in the library)" },
    { value: "Compact", label: "Compact (George R.R. Martin)" },
    { value: "Spaced", label: "Spaced (George R. R. Martin)" },
  ];

/** The settings-page wording for one handling. */
export function searchInitialsHandlingLabel(handling: SearchInitialsHandling): string {
  return SEARCH_INITIALS_HANDLING_OPTIONS.find((o) => o.value === handling)?.label ?? handling;
}
