import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Api/Dtos/UpcomingReleaseDtos.cs: UpcomingReleaseDto - source, title, sourceName
// are non-nullable; id and releaseDate are now genuinely nullable too (a roster-derived
// ("Source": "Roster") row has neither a stable id nor always a precise date - see
// AudiobookManager/UPCOMING_RELEASES_DESIGN.md). authorId/authorName/seriesId/seriesName/
// seriesPosition/sourceUrl/imageUrl remain genuinely optional (a release may be author-only,
// series-only, or both).
export type UpcomingReleaseSource = "Legacy" | "Roster";

export type UpcomingRelease = Require<
  components["schemas"]["UpcomingReleaseDto"],
  "source" | "title" | "sourceName"
> & {
  source: UpcomingReleaseSource;
};

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

// AudiobookManager.Api/Dtos/UpcomingReleaseDtos.cs (DismissRosterUpcomingReleaseDto): a plain
// [Required]-less class, so every field is optional on the wire; Title is always sent by callers
// and exactly one of seriesName/authorId is set (backend-validated).
export type DismissRosterUpcomingRelease = components["schemas"]["DismissRosterUpcomingReleaseDto"];

export type { UpcomingRelease as default };
