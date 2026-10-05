import { cloneElement, type ComponentProps, type ReactElement } from "react";
import type { VariantProps } from "class-variance-authority";

import { buttonVariants } from "@/components/ui/button";
import { cn } from "cn";

// What the cloned link receives: anchor props plus the slot marker (hyphenated names are not part
// of ComponentProps<"a">, so the clone prop type has to name it).
type LinkElementProps = ComponentProps<"a"> & { "data-slot"?: string };

type LinkButtonProps = ComponentProps<"a"> &
  VariantProps<typeof buttonVariants> & {
    /** The link to render, e.g. `<Link to="/library" />` or `<a href="/library" />`. Required: without it there is no link. */
    render: ReactElement<LinkElementProps>;
  };

/**
 * A link that looks like a `Button`: `<LinkButton render={<Link to="/library" />}>Back to Library</LinkButton>`.
 *
 * It styles the rendered element with `buttonVariants` instead of wrapping Base UI's `Button`.
 * `Button` with `nativeButton={false}` always adds `role="button"` to whatever it renders, so a
 * link built that way is announced as a button; this one stays an `<a href>` with link semantics,
 * plus middle-click, Ctrl/Cmd-click, "Open in new tab" and the URL preview.
 * Navigation must never be an `onClick` + `navigate()` on a button — see DESIGN.md section 3.
 *
 * `render` is typed as anchor-compatible props, which cannot strictly reject a non-anchor element
 * (a `<button>`'s props are structurally assignable): that it is a link is a convention, enforced
 * by review and the preset's navigate() lint rule rather than by the type.
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
  });
}
