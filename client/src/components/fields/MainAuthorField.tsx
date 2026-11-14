import { useQuery } from "@tanstack/react-query";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { NO_MAIN_AUTHOR, mainAuthorOf } from "@/helpers/mainAuthor";

export interface MainAuthorFieldProps {
  /** Every author of the book, in order. */
  authors: readonly string[];
  /** The chosen main author; "" (or a name no longer among `authors`) means none. */
  value: string;
  /** Called with the chosen author's name, or "" for none. */
  onChange: (value: string) => void;
  disabled?: boolean;
}

/**
 * Picks which of the book's authors is its main author (optional - a book with several equal
 * authors has none). The main author is the only author in the default and automated metadata
 * search query and, when the library setting is on, the only author in the book's folder name; the
 * hint says which of those applies so the effect on the path is never a surprise.
 */
export function MainAuthorField({
  authors,
  value,
  onChange,
  disabled = false,
}: MainAuthorFieldProps) {
  // The same query the settings page and the search seed use, so this is normally a cache hit.
  const { data: settings } = useQuery({
    queryKey: queryKeys.librarySettings(),
    queryFn: () => settingsApi.getLibrarySettings(),
  });

  const names = authors.filter((a) => a.trim().length > 0);
  const current = mainAuthorOf({ authors: names, mainAuthor: value });
  const items = [
    { value: NO_MAIN_AUTHOR, label: "No main author" },
    ...names.map((name) => ({ value: name, label: name })),
  ];

  return (
    <div className="min-w-0 flex-1">
      <label className="mb-1 block text-xs font-medium">Main author</label>
      <Select
        value={current ?? NO_MAIN_AUTHOR}
        onValueChange={(next) => onChange(next === null || next === NO_MAIN_AUTHOR ? "" : next)}
        disabled={disabled}
        items={items}
      >
        <SelectTrigger aria-label="Main author">
          <SelectValue placeholder="No main author" />
        </SelectTrigger>
        <SelectContent>
          {items.map((item) => (
            <SelectItem key={item.value} value={item.value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <p className="text-muted-foreground mt-1 text-xs">
        Optional. Only the main author is used when searching online sources
        {settings?.mainAuthorOnlyInPath ? ", and it is the only author in the folder name" : ""}.
      </p>
    </div>
  );
}
