import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { TagsInput } from "@/components/tags-input";
import { similarValuesApi } from "@/services/api";
import { applyHintSuggestion, findSimilarExisting } from "@/helpers/similarValueMatcher";

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
  // Entry-time duplicate prevention: flat author-name list to check a typed value against.
  // Non-critical — the hint just won't show if this fails to load. Shared via the TanStack
  // Query cache by every form instance that renders an authors field.
  const { data: authorNames = [] } = useQuery({
    queryKey: ["similarValueNames", "authors"],
    queryFn: () => similarValuesApi.getAuthorNames(),
    staleTime: 5 * 60 * 1000,
  });

  // Derived, not event-driven: a hint exists for every entry (by index) that currently has a
  // similar-but-not-identical existing name, recomputed on every relevant change. This covers
  // every way an entry can get into the array - typed one at a time, bulk-applied from a scraped
  // metadata search result, or already present when the book was loaded - not just the one chip
  // most recently typed into the field. It also can't go stale: there's no separate "which chip
  // triggered this" state to fall out of sync when an entry is edited, removed, or reordered.
  const hints = useMemo(() => {
    const hintMap = new Map<number, string>();
    value.forEach((author, index) => {
      if (!author?.trim()) return;
      const matches = findSimilarExisting(author, authorNames);
      if (matches[0] && matches[0] !== author) hintMap.set(index, matches[0]);
    });
    return hintMap;
  }, [value, authorNames]);

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
        suggestions={authorNames}
        reorderable
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
          Similar existing author: {suggestion} (click to use)
        </button>
      ))}
    </div>
  );
}
