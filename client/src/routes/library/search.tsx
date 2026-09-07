import { createFileRoute } from "@tanstack/react-router";
import { z } from "zod";
import SearchResultsPage from "@/components/library/SearchResultsPage";

const searchResultsSchema = z.object({
  q: z.string().trim().optional(),
  tab: z.enum(["all", "books", "authors", "series"]).optional(),
  page: z.coerce.number().int().positive().optional(),
});

export const Route = createFileRoute("/library/search")({
  validateSearch: searchResultsSchema,
  component: SearchResultsPage,
});
