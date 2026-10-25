import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api.Dtos.MetadataSourceOrderDto: a plain non-nullable record property. It is
// the body of PUT api/settings/metadata-source-order and its response (the order that was saved).
export type MetadataSourceOrder = Require<
  components["schemas"]["MetadataSourceOrderDto"],
  "sources"
>;
