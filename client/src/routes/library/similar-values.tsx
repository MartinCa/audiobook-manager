import { createFileRoute } from "@tanstack/react-router";
import SimilarValues from "@/components/SimilarValues";

export const Route = createFileRoute("/library/similar-values")({
  component: SimilarValues,
});
