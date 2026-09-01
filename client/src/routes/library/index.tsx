import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import BookLibrary from "@/components/BookLibrary";

const librarySearchSchema = z.object({
  q: z.string().optional(),
  page: z.coerce.number().int().positive().optional(),
});

export const Route = createFileRoute("/library/")({
  validateSearch: librarySearchSchema,
  component: BookLibrary,
});
