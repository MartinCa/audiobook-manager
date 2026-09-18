import { Link } from "@tanstack/react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, ExternalLink, Loader2, X } from "lucide-react";
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
}

export function UpcomingReleasesList({
  authorId,
  seriesId,
  showSource = false,
  emptyMessage = "No upcoming releases tracked yet.",
  page = 0,
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

  const handleRemove = async (release: UpcomingRelease) => {
    try {
      await upcomingReleasesApi.removeUpcomingRelease(release.id);
      await queryClient.invalidateQueries({ queryKey: queryKeys.upcomingReleases.all() });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    }
  };

  if (query.isLoading) {
    return (
      <div className="text-muted-foreground flex items-center justify-center py-6">
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        <span className="text-sm">Loading upcoming releases...</span>
      </div>
    );
  }

  const releases = query.data?.items ?? [];

  if (releases.length === 0) {
    return <p className="text-muted-foreground py-4 text-center text-sm">{emptyMessage}</p>;
  }

  return (
    <div className="border-border divide-y rounded-md border">
      {releases.map((release) => (
        <div
          key={release.id}
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
              <span>{formatDate(release.releaseDate)}</span>
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
  );
}

export default UpcomingReleasesList;
