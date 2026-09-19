import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/UpcomingReleaseDtos.cs: UpcomingReleaseDto - id, title, releaseDate,
// sourceName are non-nullable; authorId/authorName/seriesId/seriesName/seriesPosition/sourceUrl/
// imageUrl are all genuinely optional (a release may be author-only, series-only, or both).
export type UpcomingRelease = Require<
  components["schemas"]["UpcomingReleaseDto"],
  "id" | "title" | "releaseDate" | "sourceName"
>;

// AuthorFollowStatusDto/SeriesFollowStatusDto: isFollowed is non-nullable.
export type AuthorFollowStatus = Require<
  components["schemas"]["AuthorFollowStatusDto"],
  "isFollowed"
>;
export type SeriesFollowStatus = Require<
  components["schemas"]["SeriesFollowStatusDto"],
  "isFollowed"
>;

// AuthorMatchStatusDto: every field is genuinely optional (an unmatched author has none of them).
export type AuthorMatchStatus = components["schemas"]["AuthorMatchStatusDto"];

// AuthorMatchCandidateDto: sourceId/sourceName/name are non-nullable; sourceUrl/bookCount are
// optional.
export type AuthorMatchCandidate = Require<
  components["schemas"]["AuthorMatchCandidateDto"],
  "sourceId" | "sourceName" | "name"
>;

export type { UpcomingRelease as default };
