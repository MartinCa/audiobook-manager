import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import AuthorsList from "@/components/library/AuthorsList";

const authorsSearchSchema = z.object({
  q: z.string().optional(),
});

export const Route = createFileRoute("/library/authors/")({
  validateSearch: authorsSearchSchema,
  component: AuthorsList,
});
