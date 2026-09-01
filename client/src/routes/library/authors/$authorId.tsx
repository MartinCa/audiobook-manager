import { createFileRoute } from "@tanstack/react-router";
import AuthorDetail from "@/components/library/AuthorDetail";

export const Route = createFileRoute("/library/authors/$authorId")({
  component: AuthorDetail,
});
