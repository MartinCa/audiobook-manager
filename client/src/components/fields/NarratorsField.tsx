import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { TagsInput } from "@/components/tags-input";
import { similarValuesApi } from "@/services/api";
import { applyHintSuggestion, findSimilarExisting } from "@/helpers/similarValueMatcher";

export interface NarratorsFieldProps {
  value: string[];
  onChange: (value: string[]) => void;
  error?: string;
  /** Commit-input placeholder; the bulk-edit dialog overrides it with "Different values" for mixed previews. */
  placeholder?: string;
  disabled?: boolean;
  /**
   * Whether to render the field's own label. The bulk-edit dialog suppresses it because its
   * FieldRow already renders one; BookEditForm keeps the default (true) so its DOM is unchanged.
   */
  showLabel?: boolean;
}

export function NarratorsField({
  value,
  onChange,
  error,
  placeholder = "Narrator Name",
  disabled = false,
  showLabel = true,
}: NarratorsFieldProps) {
  const { data: narratorNames = [] } = useQuery({
    queryKey: ["similarValueNames", "narrators"],
    queryFn: () => similarValuesApi.getNarratorNames(),
    staleTime: 5 * 60 * 1000,
  });

  // Same derived approach as AuthorsField's hints: one per flagged entry (by index), recomputed
  // whenever the array or the known names change, so nothing can go stale.
  const hints = useMemo(() => {
    const hintMap = new Map<number, string>();
    value.forEach((narrator, index) => {
      if (!narrator?.trim()) return;
      const matches = findSimilarExisting(narrator, narratorNames);
      if (matches[0] && matches[0] !== narrator) hintMap.set(index, matches[0]);
    });
    return hintMap;
  }, [value, narratorNames]);

  return (
    <div className="min-w-0 flex-1">
      {showLabel && <label className="mb-1 block text-xs font-medium">Narrators</label>}
      <TagsInput
        value={value}
        onValueChange={onChange}
        suggestions={narratorNames}
        reorderable
        disabled={disabled}
        placeholder={placeholder}
        aria-invalid={Boolean(error)}
      />
      {error && <p className="text-destructive mt-1 text-xs">{error}</p>}
      {Array.from(hints.entries()).map(([index, suggestion]) => (
        <button
          key={index}
          type="button"
          className="text-muted-foreground hover:text-foreground mt-1 block text-xs underline decoration-dotted"
          onClick={() => onChange(applyHintSuggestion(value, index, suggestion))}
        >
          Similar existing narrator: {suggestion} (click to use)
        </button>
      ))}
    </div>
  );
}
