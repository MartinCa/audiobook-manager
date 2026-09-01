import { createFileRoute, redirect } from "@tanstack/react-router";

export const Route = createFileRoute("/missing-tags")({
  beforeLoad: () => {
    // TanStack Router's redirect() is a plain object the router special-cases when thrown,
    // not an Error instance.
    // eslint-disable-next-line @typescript-eslint/only-throw-error
    throw redirect({ to: "/library/missing-tags" });
  },
});
