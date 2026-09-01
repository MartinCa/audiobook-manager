import { createFileRoute } from "@tanstack/react-router";
import SeriesOverviewPage from "@/components/library/SeriesOverview";

export const Route = createFileRoute("/library/series/")({
  component: SeriesOverviewPage,
});
