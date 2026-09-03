import { createFileRoute } from "@tanstack/react-router";
import CleanBookUrls from "@/components/CleanBookUrls";

export const Route = createFileRoute("/library/clean-urls")({
  component: CleanBookUrls,
});
