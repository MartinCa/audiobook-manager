import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/FilterPresetDtos.cs: every field on FilterPresetDto is non-nullable.
// `filters` stays loosely typed on the wire (it is a bag of query-parameter values keyed by the
// list's filter names); helpers/filterPresets.ts narrows it to the filter value shapes.
export type FilterPreset = Require<
  components["schemas"]["FilterPresetDto"],
  "id" | "scope" | "name" | "filters" | "updatedAt"
>;

/** The lists that can hold presets - the backend's FilterPresetScopes. */
export type FilterPresetScope = "books" | "series" | "authors";
