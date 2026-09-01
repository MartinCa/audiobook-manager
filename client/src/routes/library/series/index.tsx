import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import SeriesOverviewPage from "@/components/library/SeriesOverview";

const seriesSearchSchema = z.object({
  q: z.string().optional(),
});

export const Route = createFileRoute("/library/series/")({
  validateSearch: seriesSearchSchema,
  component: SeriesOverviewPage,
});
