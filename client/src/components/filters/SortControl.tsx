import { ArrowDown, ArrowUp } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  effectiveSort,
  sortAfterDirectionToggle,
  sortAfterKeyChange,
  type ListSort,
  type SortConfig,
} from "@/helpers/listSort";

interface SortControlProps {
  config: SortConfig;
  /** The committed sort (the route's `sortBy`/`sortDir`); empty means the list's default order. */
  sort: ListSort;
  onChange: (next: ListSort) => void;
  /** What the list holds, for the accessible names ("Sort authors by"). */
  noun: string;
}

/**
 * A key dropdown plus an ascending/descending toggle, sitting beside a list's search and filter
 * controls. Stateless: the sort lives in the route search (like the filters), so it is linkable
 * and the page owns resetting to the first page when it changes.
 */
export function SortControl({ config, sort, onChange, noun }: SortControlProps) {
  const { sortBy, sortDir } = effectiveSort(config, sort);
  const items = config.options.map((o) => ({ value: o.value, label: o.label }));
  const ascending = sortDir === "asc";
  const DirectionIcon = ascending ? ArrowUp : ArrowDown;

  return (
    <div className="flex items-center gap-1">
      <Select
        value={sortBy}
        onValueChange={(next) => {
          if (next != null) onChange(sortAfterKeyChange(config, next));
        }}
        items={items}
      >
        <SelectTrigger aria-label={`Sort ${noun} by`}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {items.map((item) => (
            <SelectItem key={item.value} value={item.value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        type="button"
        variant="outline"
        size="icon"
        onClick={() => onChange(sortAfterDirectionToggle(config, sort))}
        aria-label={
          ascending ? "Ascending - switch to descending" : "Descending - switch to ascending"
        }
        title={ascending ? "Ascending" : "Descending"}
      >
        <DirectionIcon className="h-4 w-4" />
      </Button>
    </div>
  );
}
