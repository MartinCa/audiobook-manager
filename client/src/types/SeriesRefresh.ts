import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/SeriesRefreshDtos.cs (SeriesRefreshResultDto): success/hasChanges/
// changeCount are non-nullable; sourceName is genuinely nullable and is the metadata source's
// name (e.g. "Hardcover"), not the source's own series title.
export type SeriesRefreshResult = Require<
  components["schemas"]["SeriesRefreshResultDto"],
  "success" | "hasChanges" | "changeCount"
>;

// The wire change-kind strings the backend serializes (SeriesRefreshChangeTypeDto).
export type SeriesRefreshChangeType = "PartUpdate" | "MissingBook" | "PartRemoval";

// AudiobookManager.Api/Dtos/SeriesRefreshDtos.cs (SeriesRefreshChangeDto): every field is
// nullable on the wire (a missing book has no audiobookId, a part update has no position natural
// key, ...), so this stays the raw generated shape. changeType is the discriminated union marker.
export type SeriesRefreshChange = components["schemas"]["SeriesRefreshChangeDto"] & {
  changeType: SeriesRefreshChangeType;
};

// AudiobookManager.Api/Dtos/SeriesRefreshDtos.cs (SeriesRefreshPendingDto): seriesName on the
// wire is nullable (plain record, no [Required]), but the backend always sends it, and the
// changes array always carries the discriminated changeType strings the backend serializes
// (SeriesRefreshChangeTypeDto.ToDto). This type narrows both.
export type SeriesRefreshPending = Omit<
  components["schemas"]["SeriesRefreshPendingDto"],
  "changes"
> & {
  seriesName: string;
  sourceName: string;
  sourceUrl: string;
  sourceSeriesName?: string | null;
  fetchedAt: string;
  changes: SeriesRefreshChange[];
};

// AudiobookManager.Api/Dtos/SeriesRefreshDtos.cs (SeriesRefreshPendingListItemDto /
// SeriesRefreshPendingPageDto): seriesName/fetchedAt/changeCount and items/total are non-nullable.
export type SeriesRefreshPendingListItem = Require<
  components["schemas"]["SeriesRefreshPendingListItemDto"],
  "seriesName" | "fetchedAt" | "changeCount"
>;

export type SeriesRefreshPendingPage = Require<
  components["schemas"]["SeriesRefreshPendingPageDto"],
  "items" | "total"
> & {
  items: SeriesRefreshPendingListItem[];
};

/**
 * One change the user accepted in the review dialog. For a part update/removal audiobookId is the
 * target library book; for a missing book it is the library book the user chose to assign the
 * roster entry (position/title), and 0 means "skip".
 */
export interface SeriesRefreshApplyChange {
  changeType: SeriesRefreshChangeType;
  audiobookId: number;
  position?: string;
  title?: string;
}

export interface SeriesRefreshApplyRequest {
  adoptSourceSeriesName: boolean;
  selections: SeriesRefreshApplyChange[];
}
