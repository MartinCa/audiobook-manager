import { useMemo, type ComponentProps } from "react";
import { useQuery } from "@tanstack/react-query";
import { TypeaheadInput } from "@/components/TypeaheadInput";
import { similarValuesApi } from "@/services/api";
import { findSimilarExisting } from "@/helpers/similarValueMatcher";

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
  const { data: seriesNames = [] } = useQuery({
    queryKey: ["similarValueNames", "series"],
    queryFn: () => similarValuesApi.getSeriesNames(),
    staleTime: 5 * 60 * 1000,
  });

  // Same derived approach as the author/narrator hints: a similar existing series is shown
  // regardless of whether the value was typed, blurred into, or set in bulk via a scraped
  // metadata-search apply.
  const seriesHint = useMemo(() => {
    const series = value?.trim();
    if (!series) return null;
    const matches = findSimilarExisting(series, seriesNames);
    return matches[0] && matches[0] !== series ? matches[0] : null;
  }, [value, seriesNames]);

  return (
    <div className="min-w-0 flex-1">
      {showLabel && <label className="mb-1 block text-xs font-medium">Series</label>}
      <TypeaheadInput
        ref={ref}
        value={value}
        onValueChange={onChange}
        candidates={seriesNames}
        placeholder={placeholder}
        disabled={disabled}
        onBlur={onBlur}
      />
      {seriesHint && (
        <button
          type="button"
          className="text-muted-foreground hover:text-foreground mt-1 text-xs underline decoration-dotted"
          onClick={() => onChange(seriesHint)}
        >
          Similar existing series: {seriesHint} (click to use)
        </button>
      )}
    </div>
  );
}
