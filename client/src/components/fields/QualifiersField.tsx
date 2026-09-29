import { Button } from "@/components/ui/button";
import { useBookQualifiers } from "@/hooks/useBookQualifiers";
import { normalizeQualifiers, qualifierLabel } from "@/helpers/bookQualifiers";

export interface QualifiersFieldProps {
  /** Keys of the qualifiers currently set. */
  value: readonly string[];
  onChange: (value: string[]) => void;
  disabled?: boolean;
}

/**
 * Toggle buttons for a book's qualifiers (abridged, dramatized, ...). None are required, any
 * combination is allowed. The options come from the backend's list, so a new qualifier appears
 * here without a frontend change. A key the list no longer has stays visible so the user can see
 * it; the server drops it on the next save (it has no label to write into a name).
 */
export function QualifiersField({ value, onChange, disabled = false }: QualifiersFieldProps) {
  const options = useBookQualifiers();
  const selected = normalizeQualifiers(value, options);
  const keys = normalizeQualifiers([...options.map((o) => o.key), ...selected], options);

  const toggle = (key: string) => {
    const next = selected.includes(key) ? selected.filter((k) => k !== key) : [...selected, key];
    onChange(normalizeQualifiers(next, options));
  };

  return (
    <div className="min-w-0">
      <span id="qualifiers-label" className="mb-1 block text-xs font-medium">
        Qualifiers
      </span>
      <div role="group" aria-labelledby="qualifiers-label" className="flex flex-wrap gap-2">
        {keys.map((key) => {
          const isSelected = selected.includes(key);
          return (
            <Button
              key={key}
              type="button"
              size="sm"
              variant={isSelected ? "default" : "outline"}
              aria-pressed={isSelected}
              disabled={disabled}
              onClick={() => toggle(key)}
            >
              {qualifierLabel(key, options)}
            </Button>
          );
        })}
      </div>
    </div>
  );
}
