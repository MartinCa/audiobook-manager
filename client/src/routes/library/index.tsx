import { createFileRoute } from "@tanstack/react-router";
import BookLibrary from "@/components/BookLibrary";

export const Route = createFileRoute("/library/")({
  component: BookLibrary,
});
