import { Link } from "@tanstack/react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertCircle, CalendarClock, ExternalLink, Loader2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PAGE_SIZE } from "@/constants/paging";
import { upcomingReleasesApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import { formatDate } from "@/helpers/formatHelpers";
import type { UpcomingRelease } from "@/types/UpcomingRelease";

interface UpcomingReleasesListProps {
  authorId?: number;
  seriesId?: number;
  /** Shows the author/series name on each row - used by the consolidated view only. */
  showSource?: boolean;
  emptyMessage?: string;
  /** Zero-based page, for the consolidated view's pager. Author/series-scoped uses fit on one
   * page in practice, so they default to the first. */
  page?: number;
  /**
   * Shows a "Showing X of Y" hint when more releases exist than this one page holds. The
   * consolidated view already renders its own pager below this component with the same total,
   * so it opts out to avoid showing the count twice; the author/series-scoped uses (a single,
   * unpaged call) want it since they have no pager of their own.
   */
  showOverflowHint?: boolean;
}

export function UpcomingReleasesList({
  authorId,
  seriesId,
  showSource = false,
  emptyMessage = "No upcoming releases tracked yet.",
  page = 0,
  showOverflowHint = true,
}: UpcomingReleasesListProps) {
  const queryClient = useQueryClient();

  const query = useQuery({
    queryKey: queryKeys.upcomingReleases.page(authorId, seriesId, page),
    queryFn: () =>
      upcomingReleasesApi.getUpcomingReleases({
        authorId,
        seriesId,
        limit: PAGE_SIZE,
        offset: page * PAGE_SIZE,
      }),
    placeholderData: keepPreviousData,
  });

  // "Legacy" rows have a real UpcomingRelease row to DELETE; "Roster" rows have none (they're a
  // series/author roster entry classified Upcoming) and are dismissed by setting IsIgnored on
  // that entry instead - addressed by series name+position or by author id, matching whichever
  // roster it came from (AudiobookManager/UPCOMING_RELEASES_DESIGN.md).
  const handleRemove = async (release: UpcomingRelease) => {
    try {
      if (release.source === "Legacy") {
        if (release.id == null) return;
        await upcomingReleasesApi.removeUpcomingRelease(release.id);
      } else if (release.seriesName) {
        await upcomingReleasesApi.dismissRosterUpcomingRelease({
          seriesName: release.seriesName,
          seriesPosition: release.seriesPosition ?? undefined,
          title: release.title,
        });
      } else if (release.authorId != null) {
        await upcomingReleasesApi.dismissRosterUpcomingRelease({
          authorId: release.authorId,
          title: release.title,
        });
      } else {
        return;
      }
      await queryClient.invalidateQueries({ queryKey: queryKeys.upcomingReleases.all() });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    }
  };

  // A "Roster" row carries no stable id (roster ids are not stable across a refresh - see the
  // design doc), so the key has to be built from whatever does identify it uniquely on the page:
  // its source scope (author or series+position) plus its title.
  const releaseKey = (release: UpcomingRelease): string =>
    release.source === "Legacy"
      ? `legacy-${release.id}`
      : `roster-${release.authorId ?? ""}-${release.seriesName ?? ""}-${release.seriesPosition ?? ""}-${release.title}`;

  if (query.isLoading) {
    return (
      <div className="text-muted-foreground flex items-center justify-center py-6">
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        <span className="text-sm">Loading upcoming releases...</span>
      </div>
    );
  }

  // A failed fetch must not fall through to `emptyMessage` - "No upcoming releases tracked yet"
  // reads as a real (if unwelcome) answer, not as "something went wrong", so a network/500
  // failure would otherwise look identical to an author with nothing tracked.
  if (query.isError) {
    return (
      <div className="text-destructive flex items-center justify-center gap-1.5 py-4 text-center text-sm">
        <AlertCircle className="h-4 w-4 shrink-0" />
        {handleApiError(query.error).message}
      </div>
    );
  }

  const releases = query.data?.items ?? [];
  const total = query.data?.total ?? releases.length;

  if (releases.length === 0) {
    return <p className="text-muted-foreground py-4 text-center text-sm">{emptyMessage}</p>;
  }

  return (
    <div className="space-y-2">
      <div className="border-border divide-y rounded-md border">
        {releases.map((release) => (
          <div
            key={releaseKey(release)}
            className="hover:bg-muted/50 flex items-start gap-3 p-3 transition-colors"
          >
            {release.imageUrl ? (
              <img
                src={release.imageUrl}
                alt=""
                className="h-16 w-11 shrink-0 rounded-sm object-cover"
              />
            ) : (
              <div className="bg-muted flex h-16 w-11 shrink-0 items-center justify-center rounded-sm">
                <CalendarClock className="text-muted-foreground h-4 w-4" />
              </div>
            )}

            <div className="min-w-0 flex-1">
              <div className="text-foreground font-medium break-words">
                {release.title}
                {release.seriesPosition && (
                  <span className="text-muted-foreground ml-1.5 text-xs">
                    #{release.seriesPosition}
                  </span>
                )}
              </div>
              <div className="text-muted-foreground flex flex-wrap items-center gap-1.5 text-xs">
                {/* A precise ReleaseDate is preferred; otherwise fall back to the bare Year (a
                    roster-derived entry can carry a Year with no precise date yet - see
                    AudiobookManager/UPCOMING_RELEASES_DESIGN.md's SortDate). Neither present
                    means the source gave no timing at all. */}
                <span>
                  {release.releaseDate
                    ? formatDate(release.releaseDate)
                    : (release.year ?? "Release date unknown")}
                </span>
                {showSource && release.authorName && (
                  <>
                    <span>&middot;</span>
                    {release.authorId ? (
                      <Link
                        to="/library/authors/$authorId"
                        params={{ authorId: String(release.authorId) }}
                        className="hover:text-foreground hover:underline"
                      >
                        {release.authorName}
                      </Link>
                    ) : (
                      <span>{release.authorName}</span>
                    )}
                  </>
                )}
                {showSource && release.seriesName && (
                  <>
                    <span>&middot;</span>
                    <Link
                      to="/library/series/$seriesName"
                      params={{ seriesName: release.seriesName }}
                      className="hover:text-foreground hover:underline"
                    >
                      {release.seriesName}
                    </Link>
                  </>
                )}
                {release.sourceUrl && (
                  <a
                    href={release.sourceUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="hover:text-foreground flex items-center gap-0.5"
                  >
                    <ExternalLink className="h-3 w-3" />
                    {release.sourceName}
                  </a>
                )}
              </div>
            </div>

            <Button
              variant="ghost"
              size="icon"
              className="text-muted-foreground hover:text-destructive h-7 w-7 shrink-0"
              title="Remove from upcoming releases"
              onClick={() => {
                void handleRemove(release);
              }}
            >
              <X className="h-4 w-4" />
            </Button>
          </div>
        ))}
      </div>
      {showOverflowHint && total > releases.length && (
        <p className="text-muted-foreground text-center text-xs">
          Showing {releases.length} of {total} upcoming releases.
        </p>
      )}
    </div>
  );
}

export default UpcomingReleasesList;
