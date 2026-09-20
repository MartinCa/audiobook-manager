import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import AuthorsList from "@/components/library/AuthorsList";

const authorsSearchSchema = z.object({
  q: z.string().optional(),
  followed: z.boolean().optional(),
  matched: z.boolean().optional(),
  minBookCount: z.number().optional(),
  maxBookCount: z.number().optional(),
  hasMissingBooks: z.boolean().optional(),
  hasUpcomingBooks: z.boolean().optional(),
  refreshedAfter: z.string().optional(),
  refreshedBefore: z.string().optional(),
  neverRefreshed: z.boolean().optional(),
  sources: z.array(z.string()).optional(),
});

export const Route = createFileRoute("/library/authors/")({
  validateSearch: authorsSearchSchema,
  component: AuthorsList,
});
