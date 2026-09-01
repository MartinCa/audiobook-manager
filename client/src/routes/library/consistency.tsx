import { createFileRoute } from "@tanstack/react-router";
import LibraryConsistency from "@/components/LibraryConsistency";

export const Route = createFileRoute("/library/consistency")({
  component: LibraryConsistency,
});
