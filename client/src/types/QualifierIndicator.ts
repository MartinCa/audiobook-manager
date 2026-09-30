import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/BookQualifierDtos.cs: every field on both records is non-nullable.
export type QualifierIndicator = Require<
  components["schemas"]["QualifierIndicatorDto"],
  "source" | "indicator" | "qualifierKey"
>;

export interface QualifierIndicators {
  indicators: QualifierIndicator[];
}
