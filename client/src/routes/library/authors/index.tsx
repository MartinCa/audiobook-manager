import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import AuthorsList from "@/components/library/AuthorsList";

const authorsSearchSchema = z.object({
  q: z.string().optional(),
  followed: z.boolean().optional(),
  // The redundant "Matched" boolean filter control was removed from the authors list (Bug 5); it
  // is fully expressible through `sources` (select/exclude the "Unsupported/None" option).
  minBookCount: z.number().optional(),
  maxBookCount: z.number().optional(),
  hasMissingBooks: z.boolean().optional(),
  hasUpcomingBooks: z.boolean().optional(),
  refreshedAfter: z.string().optional(),
  refreshedBefore: z.string().optional(),
  neverRefreshed: z.boolean().optional(),
  sources: z.array(z.string()).optional(),
});

export const Route = createFileRoute("/library/authors/")({
  validateSearch: authorsSearchSchema,
  component: AuthorsList,
});
