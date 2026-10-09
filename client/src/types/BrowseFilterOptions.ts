import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

/**
 * One option of a list's "Queue status" filter - the wire value and the text to show. Both lists
 * and wording come from the backend (QueueState); the client holds none of its own.
 */
export type QueueStateOption = Require<
  components["schemas"]["QueueStateOptionDto"],
  "value" | "label"
>;

// AudiobookManager.Api/Dtos/BrowseFilterOptionsDto.cs: every list is non-nullable there, and so
// are the queue-state options inside the three queue lists.
export type BrowseFilterOptions = Omit<
  Require<
    components["schemas"]["BrowseFilterOptionsDto"],
    | "sources"
    | "genres"
    | "languages"
    | "bookQueueStates"
    | "seriesQueueStates"
    | "authorQueueStates"
  >,
  "bookQueueStates" | "seriesQueueStates" | "authorQueueStates"
> & {
  bookQueueStates: QueueStateOption[];
  seriesQueueStates: QueueStateOption[];
  authorQueueStates: QueueStateOption[];
};
