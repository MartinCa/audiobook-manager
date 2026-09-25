import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import BookDetail from "@/components/library/BookDetail";

const bookEditSearchSchema = z.object({
  // Set when navigating here from the read-only view page's "Search Online Metadata" button, so
  // the freshly-mounted BookEditForm opens that dialog itself instead of the view page trying to
  // drive it from outside the form.
  openSearch: z.boolean().optional(),
});

// The edit route reuses the existing BookEditForm and its save/delete/refresh behavior (see
// BookDetail for how the mode is derived from this path).
export const Route = createFileRoute("/library/book/$bookId/edit")({
  validateSearch: bookEditSearchSchema,
  component: BookDetail,
});
