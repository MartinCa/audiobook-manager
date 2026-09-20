import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import SeriesOverviewPage from "@/components/library/SeriesOverview";

const seriesSearchSchema = z.object({
  q: z.string().optional(),
  followed: z.boolean().optional(),
  matched: z.boolean().optional(),
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
