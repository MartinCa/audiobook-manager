import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/MetadataRefreshDtos.cs: Success/HasDifferences/Differences on
// MetadataRefreshResultDto are non-nullable; fetchedAt is a non-nullable DateTime (never null)
// on the pending DTOs and page items.
export type MetadataRefreshResult = Require<
  components["schemas"]["MetadataRefreshResultDto"],
  "success" | "hasDifferences" | "differences"
>;

export type MetadataRefreshDiff = Require<components["schemas"]["MetadataRefreshDiffDto"], "field">;

// AudiobookManager.Api/Dtos/MetadataRefreshDtos.cs: every field on PendingMetadataRefreshDto is
// non-nullable (AudiobookId/FetchedAt/SourceName/SourceUrl/Payload); fetchedAt is the UTC
// timestamp of the snapshot.
export type PendingMetadataRefresh = Require<
  components["schemas"]["PendingMetadataRefreshDto"],
  "audiobookId" | "fetchedAt" | "sourceName" | "sourceUrl" | "payload"
>;

// AudiobookManager.Api/Dtos/PendingRefreshSnapshotDto.cs: url/source/authors/narrators/
// bookName/genres are non-nullable; the rest are genuinely nullable on the record.
export type PendingRefreshSnapshot = Require<
  components["schemas"]["PendingRefreshSnapshotDto"],
  "url" | "source" | "authors" | "narrators" | "bookName" | "genres"
>;

// AudiobookManager.Api/Dtos/MetadataRefreshDtos.cs: every field on the list item is
// non-nullable (AudibookId/BookName/Authors/FetchedAt/SourceName).
export type PendingMetadataRefreshListItem = Require<
  components["schemas"]["PendingMetadataRefreshListItemDto"],
  "audiobookId" | "bookName" | "authors" | "fetchedAt" | "sourceName"
>;

export type PendingMetadataRefreshPage = Require<
  components["schemas"]["PendingMetadataRefreshPageDto"],
  "items" | "total"
>;

// Body of POST api/metadata-refresh/bulk. Omitted olderThanUtc means "never-refreshed books
// only" — the backend interprets null the same way.
export type BulkMetadataRefresh = {
  olderThanUtc?: string;
};

export type { MetadataRefreshResult as default };
