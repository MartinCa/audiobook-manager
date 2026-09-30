import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";

export interface PrimarySeriesChooserProps {
  /** The series names the source reported, canonical order. */
  options: readonly string[];
  /** The primary that will be applied: the user's choice, or the default the apply would pick. */
  value: string;
  onChange: (name: string) => void;
  /** Distinguishes the radio ids when two choosers are on screen (a list of pending rows). */
  idPrefix: string;
}

/**
 * Which of a source's several series becomes the book's primary one - the series in its tags,
 * metadata files and library path. The rest become additional series. Shown only when the source
 * reports more than one; with one there is nothing to choose.
 */
export function PrimarySeriesChooser({
  options,
  value,
  onChange,
  idPrefix,
}: PrimarySeriesChooserProps) {
  if (options.length < 2) return null;

  return (
    <fieldset className="border-border space-y-2 rounded-md border p-3 text-xs">
      <legend className="px-1 font-medium">Primary series</legend>
      <p className="text-muted-foreground">
        Used for the book&apos;s tags and file location. The others are kept as additional series.
      </p>
      <RadioGroup value={value} onValueChange={onChange} className="space-y-1">
        {options.map((name, index) => {
          const id = `${idPrefix}-primary-series-${index}`;
          return (
            <div key={name} className="flex items-center gap-2">
              <RadioGroupItem value={name} id={id} className="shrink-0" />
              <label htmlFor={id} className="min-w-0 cursor-pointer text-sm break-words">
                {name}
              </label>
            </div>
          );
        })}
      </RadioGroup>
    </fieldset>
  );
}
