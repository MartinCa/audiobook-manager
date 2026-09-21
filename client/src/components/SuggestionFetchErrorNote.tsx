import { AlertCircle } from "lucide-react";

/**
 * Shown in place of a type-ahead suggestion dropdown once a fetch (and its retry) both fail -
 * see useServerSuggestions. Styled and positioned like the dropdown it replaces (absolute,
 * popover-bordered) so TypeaheadInput and TagsInput show the same note for the same condition
 * instead of two differently-styled ones in the same edit form.
 */
export function SuggestionFetchErrorNote() {
  return (
    <p
      role="alert"
      className="border-border bg-popover text-status-error absolute top-full left-0 z-50 mt-1 flex w-max items-center gap-1 rounded-md border px-3 py-2 text-xs shadow-md"
    >
      <AlertCircle className="h-3 w-3 shrink-0" />
      <span>Couldn't load suggestions.</span>
    </p>
  );
}
