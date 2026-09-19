import { ChevronDown, SlidersHorizontal } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "cn";

interface FilterToggleButtonProps {
  expanded: boolean;
  onToggle: () => void;
  activeCount: number;
  /** Id of the panel this button expands/collapses, wired to `aria-controls`. */
  controls: string;
}

/**
 * Sits beside a list page's search box; expands/collapses an `EntityFilterBar` rendered below it.
 * The active-filter count stays visible on the badge even while collapsed, so a filtered list
 * doesn't look unexplained.
 */
export function FilterToggleButton({
  expanded,
  onToggle,
  activeCount,
  controls,
}: FilterToggleButtonProps) {
  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      onClick={onToggle}
      aria-expanded={expanded}
      aria-controls={controls}
    >
      <SlidersHorizontal className="h-4 w-4" />
      Filters
      {activeCount > 0 && (
        <Badge variant="secondary" className="ml-0.5">
          {activeCount}
        </Badge>
      )}
      <ChevronDown className={cn("h-4 w-4 transition-transform", expanded && "rotate-180")} />
    </Button>
  );
}

export default FilterToggleButton;
