import type { FilterFieldDef } from "./filterUtils";
import type { QueueStateOption } from "@/types/BrowseFilterOptions";

/**
 * The "Queue status" multiselect every library list offers (books, series, authors): where an item
 * sits in the review queues online-metadata work moves it through, so the backlog of untouched
 * items can be worked through without the ones already queued, rejected or awaiting review coming
 * back every time. The options and their wording are served by `GET /browse/filter-options`
 * (never a client list); until they arrive the field has no options, and a value already in the
 * filters (a shared link, a saved preset) still shows as its raw value.
 */
export function queueStateField(options: readonly QueueStateOption[] | undefined): FilterFieldDef {
  const served = options ?? [];
  return {
    type: "multiselect",
    key: "queueStates",
    label: "Queue status",
    options: served.map((option) => option.value),
    optionLabels: Object.fromEntries(served.map((option) => [option.value, option.label])),
  };
}
