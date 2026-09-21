import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/AudiobookSummaryDto.cs: id/authors/narrators/genres are
// non-nullable. bookName, year, and the rest are genuinely nullable on the backend record.
//
// isMatched is ALSO non-nullable on the DTO, but is deliberately left out of this Require<> list:
// SeriesOwnedBookDto now carries isMatched/matchedSourceName too (Bug 8 unification), but some
// older call sites may still build a ManagedAudiobook-shaped object without them, and
// BookListRow.tsx (the only reader of isMatched/matchedSourceName) already treats a missing
// value as unmatched.
export type ManagedAudiobook = Require<
  components["schemas"]["AudiobookSummaryDto"],
  "id" | "authors" | "narrators" | "genres"
>;

export type { ManagedAudiobook as default };
