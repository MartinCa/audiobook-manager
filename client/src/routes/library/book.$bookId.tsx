import { createFileRoute } from "@tanstack/react-router";
import BookDetail from "@/components/library/BookDetail";

// Read-only by default; the editor lives on /library/book/$bookId/edit (BookDetail derives the
// mode from the matched path).
export const Route = createFileRoute("/library/book/$bookId")({
  component: BookDetail,
});
