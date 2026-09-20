import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import BookLibrary from "@/components/BookLibrary";

const librarySearchSchema = z.object({
  q: z.string().optional(),
  page: z.coerce.number().int().positive().optional(),
  sources: z.array(z.string()).optional(),
  genres: z.array(z.string()).optional(),
  languages: z.array(z.string()).optional(),
  minDurationInSeconds: z.number().optional(),
  maxDurationInSeconds: z.number().optional(),
});

export const Route = createFileRoute("/library/")({
  validateSearch: librarySearchSchema,
  component: BookLibrary,
});
