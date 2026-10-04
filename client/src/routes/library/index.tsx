import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import { normalizeRefreshBound } from "@/components/filters/filterUtils";
import BookLibrary from "@/components/BookLibrary";

const librarySearchSchema = z.object({
  q: z.string().optional(),
  page: z.coerce.number().int().positive().optional(),
  sources: z.array(z.string()).optional(),
  genres: z.array(z.string()).optional(),
  languages: z.array(z.string()).optional(),
  qualifiers: z.array(z.string()).optional(),
  minDurationInSeconds: z.number().optional(),
  maxDurationInSeconds: z.number().optional(),
  refreshedAfter: z
    .string()
    .optional()
    .transform((v) => normalizeRefreshBound(v, "after")),
  refreshedBefore: z
    .string()
    .optional()
    .transform((v) => normalizeRefreshBound(v, "before")),
  neverRefreshed: z.boolean().optional(),
});

export const Route = createFileRoute("/library/")({
  validateSearch: librarySearchSchema,
  component: BookLibrary,
});
