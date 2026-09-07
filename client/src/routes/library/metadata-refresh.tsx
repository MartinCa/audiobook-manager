import { createFileRoute } from "@tanstack/react-router";
import MetadataRefresh from "@/components/MetadataRefresh";

export const Route = createFileRoute("/library/metadata-refresh")({
  component: MetadataRefresh,
});
