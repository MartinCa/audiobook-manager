import { createFileRoute } from "@tanstack/react-router";
import AuthorsList from "@/components/library/AuthorsList";

export const Route = createFileRoute("/library/authors/")({
  component: AuthorsList,
});
