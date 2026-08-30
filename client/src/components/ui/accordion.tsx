import type { JSX } from "react";
import { Accordion as AccordionPrimitive } from "@base-ui/react/accordion";
import { ChevronDown } from "lucide-react";

import { cn } from "@/lib/utils";

// Base UI's accordion always works on an array value with a `multiple` boolean, unlike
// Radix's `type="single" | "multiple"` with a bare string for single mode. This wrapper keeps
// the Radix-shaped call sites (a single open item as a plain string) working unchanged.
type SharedProps = Omit<
  AccordionPrimitive.Root.Props,
  "value" | "defaultValue" | "onValueChange" | "multiple"
>;

interface AccordionSingleProps extends SharedProps {
  type: "single";
  // Base UI's single mode always allows toggling the open item closed, matching how this
  // app always paired Radix's `type="single"` with `collapsible`; accepted for a
  // source-compatible call site and otherwise unused.
  collapsible?: boolean;
  value?: string;
  defaultValue?: string;
  onValueChange?: (value: string) => void;
}

interface AccordionMultipleProps extends SharedProps {
  type: "multiple";
  value?: string[];
  defaultValue?: string[];
  onValueChange?: (value: string[]) => void;
}

type AccordionProps = AccordionSingleProps | AccordionMultipleProps;

function Accordion(props: AccordionSingleProps): JSX.Element;
function Accordion(props: AccordionMultipleProps): JSX.Element;
function Accordion(props: AccordionProps) {
  if (props.type === "single") {
    // `collapsible` is accepted only for a source-compatible call site; Base UI's single
    // mode has no equivalent prop, so it's dropped rather than forwarded.
    const { collapsible, value, defaultValue, onValueChange, ...rest } = props;
    void collapsible;
    return (
      <AccordionPrimitive.Root
        data-slot="accordion"
        value={value !== undefined ? (value ? [value] : []) : undefined}
        defaultValue={defaultValue !== undefined ? (defaultValue ? [defaultValue] : []) : undefined}
        onValueChange={onValueChange && ((next: string[]) => onValueChange(next[0] ?? ""))}
        {...rest}
      />
    );
  }

  const { value, defaultValue, onValueChange, ...rest } = props;
  return (
    <AccordionPrimitive.Root
      data-slot="accordion"
      multiple
      value={value}
      defaultValue={defaultValue}
      onValueChange={onValueChange}
      {...rest}
    />
  );
}

function AccordionItem({ className, ...props }: AccordionPrimitive.Item.Props) {
  return (
    <AccordionPrimitive.Item
      data-slot="accordion-item"
      className={cn("border-b", className)}
      {...props}
    />
  );
}

function AccordionTrigger({ className, children, ...props }: AccordionPrimitive.Trigger.Props) {
  return (
    <AccordionPrimitive.Header className="flex">
      <AccordionPrimitive.Trigger
        data-slot="accordion-trigger"
        className={cn(
          "flex flex-1 cursor-pointer items-center justify-between py-4 font-medium transition-all hover:underline [&[data-panel-open]>svg]:rotate-180",
          className,
        )}
        {...props}
      >
        {children}
        <ChevronDown className="h-4 w-4 shrink-0 transition-transform duration-200" />
      </AccordionPrimitive.Trigger>
    </AccordionPrimitive.Header>
  );
}

function AccordionContent({ className, children, ...props }: AccordionPrimitive.Panel.Props) {
  return (
    <AccordionPrimitive.Panel
      data-slot="accordion-content"
      className="data-closed:animate-accordion-up data-open:animate-accordion-down overflow-hidden text-sm transition-all"
      {...props}
    >
      <div className={cn("pt-0 pb-4", className)}>{children}</div>
    </AccordionPrimitive.Panel>
  );
}

export { Accordion, AccordionItem, AccordionTrigger, AccordionContent };
