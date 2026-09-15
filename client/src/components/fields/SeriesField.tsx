import { useCallback, type ComponentProps } from "react";
import { TypeaheadInput } from "@/components/TypeaheadInput";
import { similarValuesApi } from "@/services/api";
import { useEntryStatus } from "@/hooks/useEntryStatus";
import { EntryStatusHint } from "@/components/fields/EntryStatusHint";
import { TYPEAHEAD_SUGGESTION_COUNT } from "@/constants/paging";

export interface SeriesFieldProps {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  /** Input placeholder; the bulk-edit dialog overrides it with "Different values" for mixed previews. */
  placeholder?: string;
  /** Field ref for the owning form's focus tracking; forwarded to the TypeaheadInput. */
  ref?: ComponentProps<"input">["ref"];
  /** Blur handler for the owning form's focus/blur tracking; forwarded to the TypeaheadInput. */
  onBlur?: ComponentProps<"input">["onBlur"];
  /**
   * Whether to render the field's own label. The bulk-edit dialog suppresses it because its
   * FieldRow already renders one; BookEditForm keeps the default (true) so its DOM is unchanged.
   */
  showLabel?: boolean;
}

export function SeriesField({
  value,
  onChange,
  disabled = false,
  placeholder = "Series name",
  ref,
  onBlur,
  showLabel = true,
}: SeriesFieldProps) {
  // The type-ahead candidates come from a bounded server-side lookup keyed by the typed query
  // (TypeaheadInput's fetchCandidates), not a preloaded list of every existing series value. The
  // exact-existing / similar / new indicator below uses its own single-value bounded
  // classification - never a client scan of the whole library's name set. The provider identity
  // is stable (it closes over nothing changeable) so TypeaheadInput's debounced fetch is not
  // reset on every render.
  const fetchCandidates = useCallback(
    (query: string) =>
      similarValuesApi.getAutocomplete("series", query, TYPEAHEAD_SUGGESTION_COUNT),
    [],
  );
  const { status, isError } = useEntryStatus("series", value);

  return (
    <div className="min-w-0 flex-1">
      {showLabel && <label className="mb-1 block text-xs font-medium">Series</label>}
      <TypeaheadInput
        ref={ref}
        value={value}
        onValueChange={onChange}
        placeholder={placeholder}
        disabled={disabled}
        onBlur={onBlur}
        fetchCandidates={fetchCandidates}
      />
      <EntryStatusHint status={status} isError={isError} onUseMatch={onChange} />
    </div>
  );
}
