import type { ReactNode } from "react";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { cn } from "cn";

interface CollapsibleCountSectionProps {
  /** The section's noun, e.g. "Missing Books" - the count is appended as "(N)". */
  label: string;
  count: number;
  /** Extra classes for the trigger text - used to color a section's heading (e.g. the amber
   *  "Missing Books" / muted "Upcoming Books" treatment series/author detail already use). */
  labelClassName?: string;
  /** Collapsed by default everywhere this is used (Missing/Upcoming Books) - only the label and
   *  count show until the user expands it. */
  defaultOpen?: boolean;
  children: ReactNode;
}

/**
 * A single collapsed-by-default "Label (N)" section: a clickable header that expands to its body.
 * Wraps the vendored Accordion primitive (single item, uncontrolled) rather than hand-rolling
 * open/close state and a11y - see DESIGN.md section 3 ("wrap for different behaviour"). Used by
 * the series and author detail pages' Missing/Upcoming Books sections so both stay closed until
 * the user asks to see them.
 */
export function CollapsibleCountSection({
  label,
  count,
  labelClassName,
  defaultOpen = false,
  children,
}: CollapsibleCountSectionProps) {
  return (
    <Accordion defaultValue={defaultOpen ? ["section"] : []}>
      <AccordionItem value="section" className="border-none">
        <AccordionTrigger
          className={cn(
            "text-lg font-bold no-underline hover:no-underline focus-visible:no-underline",
            labelClassName,
          )}
        >
          {label} ({count})
        </AccordionTrigger>
        <AccordionContent>
          <div className="space-y-4 pt-1">{children}</div>
        </AccordionContent>
      </AccordionItem>
    </Accordion>
  );
}

export default CollapsibleCountSection;
