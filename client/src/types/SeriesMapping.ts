import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Domain/SeriesMapping.cs: regex/mappedSeries/warnAboutPart are non-nullable;
// id is nullable — the same model doubles as the create-request body (id: null) and the
// list/update-response body (id always populated for a persisted row), which is why this file
// splits it into a request-shaped base and a response-shaped extension instead of aliasing the
// schema directly.
export type SeriesMappingBase = Require<
  components["schemas"]["SeriesMapping"],
  "regex" | "mappedSeries" | "warnAboutPart"
>;

export interface SeriesMapping extends SeriesMappingBase {
  id: number;
}

// AudiobookManager.Api/Dtos/SeriesMappingGroupDtos.cs: grouping is done server-side now (the
// client used to reduce the flat table into buckets itself).
export type SeriesMappingGroup = Require<
  components["schemas"]["SeriesMappingGroupDto"],
  "mappedSeries" | "items"
> & {
  items: SeriesMapping[];
};

export type SeriesMappingGroups = Require<
  components["schemas"]["SeriesMappingGroupsDto"],
  "items" | "total"
> & {
  items: SeriesMappingGroup[];
};
