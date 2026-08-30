import { createFileRoute } from "@tanstack/react-router";
import DiscoveredAudiobooks from "@/components/library/DiscoveredAudiobooks";

export const Route = createFileRoute("/library/discovered")({
  component: DiscoveredAudiobooks,
});
