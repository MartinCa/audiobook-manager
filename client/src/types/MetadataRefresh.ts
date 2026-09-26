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
// non-nullable (AudibookId/BookName/Authors/FetchedAt/SourceName/ChangedFields).
export type PendingMetadataRefreshListItem = Require<
  components["schemas"]["PendingMetadataRefreshListItemDto"],
  "audiobookId" | "bookName" | "authors" | "fetchedAt" | "sourceName" | "changedFields"
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

// AudiobookManager.Api/Dtos/MetadataRefreshDtos.cs: Processed/Updated/Removed on
// MetadataRefreshReevaluateResultDto are non-nullable.
export type MetadataRefreshReevaluateResult = Require<
  components["schemas"]["MetadataRefreshReevaluateResultDto"],
  "processed" | "updated" | "removed"
>;

// AudiobookManager.Api/Dtos/MetadataRefreshDtos.cs: Dismissed on
// DismissSelectedMetadataRefreshResultDto is non-nullable.
export type DismissSelectedMetadataRefreshResult = Require<
  components["schemas"]["DismissSelectedMetadataRefreshResultDto"],
  "dismissed"
>;

// Every field name a pending snapshot can offer as a change (AudiobookManager.Services/
// MetadataRefreshFields.cs) - the vocabulary the filter picker and per-book field badges use.
export const METADATA_REFRESH_FIELDS = [
  "Authors",
  "Narrators",
  "BookName",
  "Subtitle",
  "Series",
  "SeriesPart",
  "Year",
  "Genres",
  "Description",
  "Language",
  "Rating",
  "Copyright",
  "Publisher",
  "Asin",
  "Www",
] as const;

export type MetadataRefreshFieldName = (typeof METADATA_REFRESH_FIELDS)[number];

/** Display label for a stored changed-field name; SeriesPart folds into "Series" since the two are edited together. */
export const METADATA_REFRESH_FIELD_LABELS: Record<string, string> = {
  Authors: "Authors",
  Narrators: "Narrators",
  BookName: "Book Name",
  Subtitle: "Subtitle",
  Series: "Series",
  SeriesPart: "Series Part",
  Year: "Year",
  Genres: "Genres",
  Description: "Description",
  Language: "Language",
  Rating: "Rating",
  Copyright: "Copyright",
  Publisher: "Publisher",
  Asin: "ASIN",
  Www: "URL",
};

export type { MetadataRefreshResult as default };
