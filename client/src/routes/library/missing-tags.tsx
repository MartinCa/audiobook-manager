import { createFileRoute } from "@tanstack/react-router";
import MissingTags from "@/components/MissingTags";

export const Route = createFileRoute("/library/missing-tags")({
  component: MissingTags,
});
