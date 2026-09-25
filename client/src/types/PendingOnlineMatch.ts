import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/PendingOnlineMatchDtos.cs: every field on the list item is
// non-nullable (AudiobookId/BookName/Authors/SearchedAt/SourceNames/Results).
export type PendingOnlineMatchListItem = Require<
  components["schemas"]["PendingOnlineMatchListItemDto"],
  "audiobookId" | "bookName" | "authors" | "searchedAt" | "sourceNames" | "results"
>;

export type PendingOnlineMatchPage = Require<
  components["schemas"]["PendingOnlineMatchPageDto"],
  "items" | "total"
>;

export type { PendingOnlineMatchListItem as default };
