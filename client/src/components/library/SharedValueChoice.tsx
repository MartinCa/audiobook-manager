import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { cn } from "@/lib/utils";
import type { SharedChoiceOption } from "@/helpers/bulkEdit";

// The "leave this field alone" choice of the radios; a real value is never called this.
const UNCHANGED = "__unchanged__";

export interface SharedValueChoiceProps {
  /** The values every selected book has - the only ones that can be offered. */
  options: SharedChoiceOption[];
  /** Appended, in parentheses, to a value every book is already set to; that row is disabled. */
  alreadySetLabel: string;
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
  options,
  alreadySetLabel,
  value,
  onChange,
  emptyText,
  legend,
  idPrefix,
  warning,
}: SharedValueChoiceProps) {
  if (options.length === 0) {
    return <p className="text-muted-foreground text-xs italic">{emptyText}</p>;
  }
  const items = [
    { label: "Leave unchanged", radio: UNCHANGED, id: `${idPrefix}-unchanged`, alreadySet: false },
    ...options.map((option, index) => ({
      label: option.value,
      radio: option.value,
      id: `${idPrefix}-${index}`,
      alreadySet: option.alreadySet,
    })),
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
            <RadioGroupItem
              value={item.radio}
              id={item.id}
              disabled={item.alreadySet}
              className="mt-0.5 shrink-0"
            />
            <label
              htmlFor={item.id}
              className={cn(
                "min-w-0 cursor-pointer text-xs [overflow-wrap:anywhere]",
                item.radio === UNCHANGED && "text-muted-foreground italic",
                item.alreadySet && "text-muted-foreground cursor-default",
              )}
            >
              {item.label}
              {item.alreadySet && (
                <>
                  {" "}
                  <span className="italic">({alreadySetLabel})</span>
                </>
              )}
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
