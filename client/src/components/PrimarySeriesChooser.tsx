import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import type { SeriesEntry } from "@/helpers/seriesRelations";

export interface PrimarySeriesChooserProps {
  /** The series the source reported, canonical order. */
  options: readonly SeriesEntry[];
  /** The primary that will be applied: the user's choice, or the default the apply would pick. */
  value: string;
  onChange: (name: string) => void;
  /** Distinguishes the radio ids when two choosers are on screen (a list of pending rows). */
  idPrefix: string;
}

/**
 * Which of a source's several series becomes the book's primary one - the series in its tags,
 * metadata files and library path; the rest become additional series. Rendered in the Series
 * row's "New Value" cell (see MetadataFieldDiffTable), so the choice sits on the row it changes
 * rather than below a scrolling table where it is out of sight.
 */
export function PrimarySeriesChooser({
  options,
  value,
  onChange,
  idPrefix,
}: PrimarySeriesChooserProps) {
  return (
    <fieldset className="max-w-full min-w-0 space-y-1">
      <legend className="text-muted-foreground mb-1 text-[11px] font-normal">
        Choose the primary series (tags and file location)
      </legend>
      <RadioGroup value={value} onValueChange={onChange} className="space-y-1">
        {options.map((option, index) => {
          const id = `${idPrefix}-primary-series-${index}`;
          return (
            <div key={option.name} className="flex items-start gap-2">
              <RadioGroupItem
                value={option.name}
                id={id}
                aria-describedby={option.part ? `${id}-part` : undefined}
                className="mt-0.5 shrink-0"
              />
              {/* Name and part share one wrapping label so a long name pushes neither the part nor
                  the cell's edge; overflow-wrap:anywhere breaks an unbreakable long word. The part
                  is aria-hidden here so the radio's accessible name stays the series name, and is
                  announced as its description instead. */}
              <label htmlFor={id} className="min-w-0 cursor-pointer [overflow-wrap:anywhere]">
                {option.name}
                {option.part && (
                  <span aria-hidden="true" className="text-muted-foreground">
                    {" "}
                    #{option.part}
                  </span>
                )}
              </label>
              {option.part && (
                <span id={`${id}-part`} className="sr-only">
                  Part {option.part}
                </span>
              )}
            </div>
          );
        })}
      </RadioGroup>
    </fieldset>
  );
}
