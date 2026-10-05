import { cloneElement, type ComponentProps, type ReactElement, type ReactNode } from "react";
import type { VariantProps } from "class-variance-authority";

import { buttonVariants } from "@/components/ui/button";
import { cn } from "cn";

type LinkButtonProps = Omit<ComponentProps<"a">, "children"> &
  VariantProps<typeof buttonVariants> & {
    /** The link to render, e.g. `<Link to="/library" />` or `<a href="/library" />`. Required: without it there is no link. */
    render: ReactElement<{ className?: string }>;
    children?: ComponentProps<"a">["children"];
  };

/**
 * A link that looks like a `Button`: `<LinkButton render={<Link to="/library" />}>Back to Library</LinkButton>`.
 *
 * It styles the rendered element with `buttonVariants` instead of wrapping Base UI's `Button`.
 * `Button` with `nativeButton={false}` always adds `role="button"` to whatever it renders, so a
 * link built that way is announced as a button; this one stays an `<a href>` with link semantics,
 * plus middle-click, Ctrl/Cmd-click, "Open in new tab" and the URL preview.
 * Navigation must never be an `onClick` + `navigate()` on a button — see DESIGN.md section 3.
 */
export function LinkButton({
  render,
  variant,
  size,
  className,
  children,
  ...props
}: LinkButtonProps) {
  return cloneElement(render, {
    "data-slot": "button",
    ...props,
    className: cn(buttonVariants({ variant, size }), render.props.className, className),
    // Only override the element's own children when LinkButton was given some: a third
    // cloneElement argument replaces them even when it is undefined.
    ...(children !== undefined && { children }),
  } as Partial<{ className: string; children: ReactNode }>);
}
