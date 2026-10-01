import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/AuthorRefreshPendingDtos.cs (AuthorRefreshPendingDto): authorId,
// authorName, proposedName, sourceName and fetchedAt are non-nullable record properties;
// sourceUrl is genuinely nullable (a source may report no author page).
export type AuthorRefreshPending = Require<
  components["schemas"]["AuthorRefreshPendingDto"],
  "authorId" | "authorName" | "proposedName" | "sourceName" | "fetchedAt"
>;

// AuthorRefreshPendingPageDto: items and totalCount are non-nullable.
export type AuthorRefreshPendingPage = Omit<
  Require<components["schemas"]["AuthorRefreshPendingPageDto"], "items" | "totalCount">,
  "items"
> & {
  items: AuthorRefreshPending[];
};
