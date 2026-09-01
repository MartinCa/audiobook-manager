import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import SeriesDetail from "@/components/library/SeriesDetail";

const seriesDetailSearchSchema = z.object({
  authorId: z.coerce.number().optional(),
});

export const Route = createFileRoute("/library/series/$seriesName")({
  validateSearch: seriesDetailSearchSchema,
  component: SeriesDetail,
});
