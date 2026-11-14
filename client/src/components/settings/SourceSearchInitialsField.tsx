import { useQuery } from "@tanstack/react-query";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { metadataSearchApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import {
  SEARCH_INITIALS_HANDLING_OPTIONS,
  searchInitialsHandlingLabel,
} from "@/constants/searchInitialsHandling";
import type { SearchInitialsHandling } from "@/types/LibrarySettings";

/** Value of the "follow the library-wide setting" item; a source name is never an option value. */
const USE_LIBRARY_DEFAULT = "__library-default__";

export interface SourceSearchInitialsFieldProps {
  /** The overrides as currently edited, keyed by source name; a source not in it follows the library-wide one. */
  value: Record<string, SearchInitialsHandling>;
  /** The library-wide handling, named on the "follow the library default" option. */
  libraryDefault: SearchInitialsHandling | null;
  onChange: (next: Record<string, SearchInitialsHandling>) => void;
  disabled?: boolean;
}

/**
 * One initials-handling select per metadata source, for the default and automated search query
 * sent to that source. The sources come from the backend (never a list of names here), so a newly
 * added scraper shows up with no change on this page. An override stored for a source that is not
 * listed (it was removed, or the list failed to load) is left alone: `onChange` only ever edits the
 * one source that was touched.
 */
export function SourceSearchInitialsField({
  value,
  libraryDefault,
  onChange,
  disabled = false,
}: SourceSearchInitialsFieldProps) {
  const { data: services = [] } = useQuery({
    queryKey: queryKeys.metadataServices(),
    queryFn: () => metadataSearchApi.getServices(),
  });

  if (services.length === 0) return null;

  const defaultLabel = libraryDefault
    ? `Library default (${searchInitialsHandlingLabel(libraryDefault)})`
    : "Library default";
  const items = [
    { value: USE_LIBRARY_DEFAULT, label: defaultLabel },
    ...SEARCH_INITIALS_HANDLING_OPTIONS,
  ];

  const handleChange = (source: string, next: string | null) => {
    const copy = { ...value };
    if (next === null || next === USE_LIBRARY_DEFAULT) {
      delete copy[source];
    } else {
      copy[source] = next as SearchInitialsHandling;
    }
    onChange(copy);
  };

  return (
    <div className="space-y-2" role="group" aria-label="Search initials handling per source">
      <p className="text-xs font-medium">Per source</p>
      <p className="text-muted-foreground text-xs">
        Override the setting above for a single source. A source left on the library default follows
        it.
      </p>
      <div className="space-y-2">
        {services.map((service) => (
          <div
            key={service.name}
            className="flex flex-col gap-1 sm:flex-row sm:items-center sm:gap-3"
          >
            <span className="min-w-0 text-sm sm:w-28 sm:shrink-0">{service.name}</span>
            <Select
              value={value[service.name] ?? USE_LIBRARY_DEFAULT}
              onValueChange={(next) => handleChange(service.name, next)}
              items={items}
              disabled={disabled}
            >
              <SelectTrigger
                className="w-full sm:w-96"
                aria-label={`${service.name} initials handling`}
              >
                <SelectValue placeholder="Library default" />
              </SelectTrigger>
              <SelectContent>
                {items.map((item) => (
                  <SelectItem key={item.value} value={item.value}>
                    {item.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        ))}
      </div>
    </div>
  );
}
