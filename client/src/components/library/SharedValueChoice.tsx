import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { cn } from "@/lib/utils";

// The "leave this field alone" choice of the radios; a real value is never called this.
const UNCHANGED = "__unchanged__";

export interface SharedValueChoiceProps {
  /** The values every selected book has - the only ones that can be offered. */
  values: string[];
  /** The picked value, or undefined for "Leave unchanged". */
  value: string | undefined;
  onChange: (value: string | undefined) => void;
  /** Shown instead of the choices when no value is shared. */
  emptyText: string;
  /** Names the group for assistive tech (the visible label is the surrounding FieldRow's). */
  legend: string;
  /** Distinguishes the radio ids when two choices are on screen. */
  idPrefix: string;
  /** A problem with the current pick, shown under the choices. */
  warning?: string;
}

/**
 * A bulk edit field that sets one value across the selection, chosen from the values every
 * selected book already has (a main author is always one of the book's authors; a primary series
 * is one the book is already in). With none in common there is nothing to pick, so it says so.
 * "Leave unchanged" is the default: these fields are optional, and an untouched one changes nothing.
 */
export function SharedValueChoice({
  values,
  value,
  onChange,
  emptyText,
  legend,
  idPrefix,
  warning,
}: SharedValueChoiceProps) {
  if (values.length === 0) {
    return <p className="text-muted-foreground text-xs italic">{emptyText}</p>;
  }
  const items = [
    { label: "Leave unchanged", radio: UNCHANGED, id: `${idPrefix}-unchanged` },
    ...values.map((name, index) => ({ label: name, radio: name, id: `${idPrefix}-${index}` })),
  ];
  return (
    <fieldset className="min-w-0 space-y-1">
      <legend className="sr-only">{legend}</legend>
      <RadioGroup
        value={value ?? UNCHANGED}
        onValueChange={(next: string) => onChange(next === UNCHANGED ? undefined : next)}
        className="space-y-1"
      >
        {items.map((item) => (
          <div key={item.id} className="flex items-start gap-2">
            <RadioGroupItem value={item.radio} id={item.id} className="mt-0.5 shrink-0" />
            <label
              htmlFor={item.id}
              className={cn(
                "min-w-0 cursor-pointer text-xs [overflow-wrap:anywhere]",
                item.radio === UNCHANGED && "text-muted-foreground italic",
              )}
            >
              {item.label}
            </label>
          </div>
        ))}
      </RadioGroup>
      {warning && (
        <p role="alert" className="text-destructive text-[11px]">
          {warning}
        </p>
      )}
    </fieldset>
  );
}

export default SharedValueChoice;
