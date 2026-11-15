import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { NO_MAIN_AUTHOR } from "@/helpers/mainAuthor";

export interface MainAuthorChooserProps {
  /** The authors the source reported, in its order. */
  options: readonly string[];
  /** The main author that will be applied: the user's pick, or what the library's rules would choose. "" = none. */
  value: string;
  /** Called with the author's name, or "" for no main author. */
  onChange: (name: string) => void;
  /** Distinguishes the radio ids when two choosers are on screen (a list of pending rows). */
  idPrefix: string;
  /** Set when the rules ask the user to confirm: names the author the source gave as the main one. */
  askNote?: string;
}

/**
 * Which of a source's several authors is the book's main author - the one used for the default
 * search query and, with the library setting on, the author folder. Optional: "No main author" is a
 * real choice (an anthology has none). Rendered in the Authors row's "New Value" cell (see
 * MetadataFieldDiffTable), under the list of names, so the choice sits on the row it belongs to.
 */
export function MainAuthorChooser({
  options,
  value,
  onChange,
  idPrefix,
  askNote,
}: MainAuthorChooserProps) {
  const items = [
    ...options.map((name, index) => ({
      name,
      radio: name,
      id: `${idPrefix}-main-author-${index}`,
    })),
    { name: "No main author", radio: NO_MAIN_AUTHOR, id: `${idPrefix}-main-author-none` },
  ];
  return (
    <fieldset className="max-w-full min-w-0 space-y-1">
      <legend className="text-muted-foreground mb-1 text-[11px] font-normal">
        Choose the main author (optional)
      </legend>
      {askNote && <p className="text-muted-foreground mb-1 text-[11px]">{askNote}</p>}
      <RadioGroup
        value={value === "" ? NO_MAIN_AUTHOR : value}
        onValueChange={(next: string) => onChange(next === NO_MAIN_AUTHOR ? "" : next)}
        className="space-y-1"
      >
        {items.map((item) => (
          <div key={item.id} className="flex items-start gap-2">
            <RadioGroupItem value={item.radio} id={item.id} className="mt-0.5 shrink-0" />
            <label
              htmlFor={item.id}
              className={`min-w-0 cursor-pointer [overflow-wrap:anywhere] ${
                item.radio === NO_MAIN_AUTHOR ? "text-muted-foreground italic" : ""
              }`}
            >
              {item.name}
            </label>
          </div>
        ))}
      </RadioGroup>
    </fieldset>
  );
}
