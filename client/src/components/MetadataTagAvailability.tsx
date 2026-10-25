import type { ComponentType } from "react";
import { CircleCheck, CircleQuestionMark, CircleX, FileText, Tags } from "lucide-react";
import type { LucideProps } from "lucide-react";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { cn } from "cn";

/** What a source's search response said about one tag: true/false as reported, null/undefined when it does not say. */
type Availability = boolean | null | undefined;

interface MetadataTagAvailabilityProps {
  hasDescription?: Availability;
  hasGenres?: Availability;
}

const STATES = {
  present: { Badge: CircleCheck, className: "text-status-ok", text: "available" },
  missing: { Badge: CircleX, className: "text-status-error", text: "not provided by the source" },
  unknown: {
    Badge: CircleQuestionMark,
    className: "text-status-unknown",
    text: "not included in search results",
  },
} as const;

function stateOf(value: Availability): keyof typeof STATES {
  if (value === true) return "present";
  if (value === false) return "missing";
  return "unknown";
}

interface TagIndicatorProps {
  label: string;
  Icon: ComponentType<LucideProps>;
  value: Availability;
}

/** The tag's own icon with a small status badge on its corner; the badge shape (check / cross /
 * question mark) carries the state as well as the colour. */
function TagIndicator({ label, Icon, value }: TagIndicatorProps) {
  const state = STATES[stateOf(value)];
  const description = `${label}: ${state.text}`;

  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <span
            role="img"
            aria-label={description}
            data-testid={`tag-${label.toLowerCase()}`}
            data-state={stateOf(value)}
            className={cn("relative inline-flex", state.className)}
          />
        }
      >
        <Icon className="h-4 w-4" aria-hidden="true" />
        <state.Badge
          className="bg-card absolute -right-1 -bottom-1 h-2.5 w-2.5 rounded-full"
          aria-hidden="true"
        />
      </TooltipTrigger>
      <TooltipContent>{description}</TooltipContent>
    </Tooltip>
  );
}

/**
 * Which tags a metadata search result is known to carry, without a details request per result:
 * green when the source's search response says it has the tag, red when it says it does not, grey
 * when that response does not say (not every source puts every tag in its search results).
 */
export function MetadataTagAvailability({
  hasDescription,
  hasGenres,
}: MetadataTagAvailabilityProps) {
  return (
    <div className="flex items-center gap-2" data-testid="tag-availability">
      <TagIndicator label="Description" Icon={FileText} value={hasDescription} />
      <TagIndicator label="Genres" Icon={Tags} value={hasGenres} />
    </div>
  );
}

export default MetadataTagAvailability;
