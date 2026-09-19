import { createFileRoute } from "@tanstack/react-router";
import UpcomingReleasesPage from "@/components/library/UpcomingReleasesPage";

export const Route = createFileRoute("/library/upcoming-releases")({
  component: UpcomingReleasesPage,
});
