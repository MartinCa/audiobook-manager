import { createFileRoute } from "@tanstack/react-router";
import BookList from "@/components/BookList";

export const Route = createFileRoute("/")({
  component: BookList,
});
