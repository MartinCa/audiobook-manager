import { createFileRoute } from "@tanstack/react-router";
import BookDetail from "@/components/library/BookDetail";

export const Route = createFileRoute("/library/book/$bookId")({
  component: BookDetail,
});
