import { createFileRoute } from "@tanstack/react-router";
import BookDetail from "@/components/library/BookDetail";

// The edit route reuses the existing BookEditForm and its save/delete/refresh behavior (see
// BookDetail for how the mode is derived from this path).
export const Route = createFileRoute("/library/book/$bookId/edit")({
  component: BookDetail,
});
