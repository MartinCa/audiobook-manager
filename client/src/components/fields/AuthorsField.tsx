import { useCallback } from "react";
import { TagsInput } from "@/components/tags-input";
import { similarValuesApi } from "@/services/api";
import { useEntryStatus } from "@/hooks/useEntryStatus";
import { EntryStatusHint } from "@/components/fields/EntryStatusHint";
import { applyHintSuggestion } from "@/helpers/similarValueMatcher";
import { TYPEAHEAD_SUGGESTION_COUNT } from "@/constants/paging";

export interface AuthorsFieldProps {
  value: string[];
  onChange: (value: string[]) => void;
  error?: string;
  /** Commit-input placeholder; the bulk-edit dialog overrides it with "Different values" for mixed previews. */
  placeholder?: string;
  /**
   * Whether to render the field's own label. The bulk-edit dialog suppresses it because its
   * FieldRow already renders one; BookEditForm keeps the default (true) so its DOM is unchanged.
   */
  showLabel?: boolean;
}

export function AuthorsField({
  value,
  onChange,
  error,
  placeholder = "Author Name, Second Author",
  showLabel = true,
}: AuthorsFieldProps) {
  // The type-ahead suggestions come from a bounded server-side lookup keyed by the typed query
  // (see TagsInput's suggestionsProvider), not a preloaded list of every existing author. The
  // exact-existing / similar / new indicators use their own single-value bounded classification -
  // never a client scan of the whole library's name set. The provider identity is stable (it
  // closes over nothing changeable) so TagsInput's debounced fetch is not reset on every render.
  const fetchSuggestions = useCallback(
    (query: string) =>
      similarValuesApi.getAutocomplete("author", query, TYPEAHEAD_SUGGESTION_COUNT),
    [],
  );

  return (
    <div className="min-w-0 flex-1">
      {showLabel && (
        <label className="mb-1 block text-xs font-medium">
          Authors <span className="text-destructive">*</span>
        </label>
      )}
      <TagsInput
        value={value}
        onValueChange={onChange}
        reorderable
        placeholder={placeholder}
        aria-invalid={Boolean(error)}
        suggestionsProvider={fetchSuggestions}
      />
      {error && <p className="text-destructive mt-1 text-xs">{error}</p>}
      <div className="mt-1 space-y-0.5">
        {value.map((author, index) =>
          author.trim() ? (
            <AuthorEntryStatus
              key={`${index}-${author}`}
              entry={author}
              onUseMatch={(suggestion) => onChange(applyHintSuggestion(value, index, suggestion))}
            />
          ) : null,
        )}
      </div>
    </div>
  );
}

/**
 * One author entry's exact/similar/new indicator. A dedicated hook instance per entry gives each
 * its own cached, debounced `entry-status` query, so typed and bulk-applied entries alike are
 * classified - and the call itself stays out of `.map`'s render path. Query failures surface as an
 * explicit error note rather than a silent "new" guess.
 */
function AuthorEntryStatus({
  entry,
  onUseMatch,
}: {
  entry: string;
  onUseMatch: (name: string) => void;
}) {
  const { status, isError } = useEntryStatus("author", entry);
  return <EntryStatusHint status={status} isError={isError} onUseMatch={onUseMatch} />;
}
