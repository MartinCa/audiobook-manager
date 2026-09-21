import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import SeriesOverviewPage from "@/components/library/SeriesOverview";

const seriesSearchSchema = z.object({
  q: z.string().optional(),
  followed: z.boolean().optional(),
  // The redundant "Matched" boolean filter control was removed from the series list (Bug 5); it
  // is fully expressible through `sources` (select/exclude the "Unsupported/None" option). No
  // longer a route search param either - SeriesMatchDialog's own `{ matched: false }` filter is a
  // direct API call, not routed through this search schema.
  minOwnedBooks: z.number().optional(),
  maxOwnedBooks: z.number().optional(),
  hasMissingBooks: z.boolean().optional(),
  hasUpcomingBooks: z.boolean().optional(),
  refreshedAfter: z.string().optional(),
  refreshedBefore: z.string().optional(),
  neverRefreshed: z.boolean().optional(),
  sources: z.array(z.string()).optional(),
});

export const Route = createFileRoute("/library/series/")({
  validateSearch: seriesSearchSchema,
  component: SeriesOverviewPage,
});
