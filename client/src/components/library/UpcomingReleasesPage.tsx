import { useEffect, useRef, useState } from "react";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, Loader2, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PAGE_SIZE } from "@/constants/paging";
import { OperationKeys } from "@/constants/signalrEvents";
import { LibraryViewTabs } from "./LibraryViewTabs";
import { UpcomingReleasesList } from "./UpcomingReleasesList";
import { operationsApi, upcomingReleasesApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useClampedPage } from "@/hooks/useClampedPage";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";

/**
 * The consolidated upcoming-releases view: every release discovered for any followed author or
 * series, across the whole library. Author/series-scoped views live on their own detail pages
 * (see AuthorDetail/SeriesDetail); this is the "everything, at once" surface.
 */
export function UpcomingReleasesPage() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(0);

  // Shares its query key with UpcomingReleasesList's own fetch below (same
  // authorId/seriesId/page triple), so this only reads the total off the cache the list's query
  // already populates rather than issuing a second network request.
  const pageQuery = useQuery({
    queryKey: queryKeys.upcomingReleases.page(undefined, undefined, page),
    queryFn: () =>
      upcomingReleasesApi.getUpcomingReleases({ limit: PAGE_SIZE, offset: page * PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });

  const totalCount = pageQuery.data?.total ?? 0;
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount - 1);
  // Pulls the raw page state back into range once a response shows the total shrank under it
  // (e.g. the last release on the last page was removed), so the next fetch - not just the
  // display - lands on a valid page.
  useClampedPage(page, pageCount, setPage);

  // Fire-and-forget on the backend (the sweep can run for minutes at the Hardcover rate limit's
  // pace), so this follows it the same way MissingTags follows the language backfill: poll the
  // shared operation-status endpoint while it runs, and react to the running -> not-running
  // transition rather than awaiting the POST itself.
  const { data: refreshStatus } = useQuery({
    queryKey: queryKeys.upcomingReleasesRefreshStatus(),
    queryFn: () => operationsApi.getStatus(OperationKeys.upcomingReleasesRefresh),
    refetchInterval: (query) => (query.state.data?.isRunning ? 1500 : false),
  });

  const isRefreshing = Boolean(refreshStatus?.isRunning);
  const prevRefreshingRef = useRef(false);

  useEffect(() => {
    if (prevRefreshingRef.current && !isRefreshing) {
      notifications.success("Checked followed authors and series for new releases");
      void queryClient.invalidateQueries({ queryKey: queryKeys.upcomingReleases.all() });
    }
    prevRefreshingRef.current = isRefreshing;
  }, [isRefreshing, queryClient]);

  const handleRefresh = async () => {
    try {
      await upcomingReleasesApi.refreshUpcomingReleases();
      // The sweep can start and finish between the POST above and this tab's next status poll,
      // so the running -> not-running transition the effect above watches for may never fire -
      // every poll would see isRunning: false and there is nothing to transition from. Await the
      // status refetch directly and, if it already reports not-running, treat that as completion
      // ourselves rather than relying solely on the transition check. staleTime: 0 is required
      // here - the app's default 30s staleTime would otherwise let fetchQuery return the
      // pre-refresh cached value with no network call at all, firing a false "finished" the
      // instant the sweep starts.
      const result = await queryClient.fetchQuery({
        queryKey: queryKeys.upcomingReleasesRefreshStatus(),
        queryFn: () => operationsApi.getStatus(OperationKeys.upcomingReleasesRefresh),
        staleTime: 0,
      });
      if (!result.isRunning) {
        notifications.success("Checked followed authors and series for new releases");
        void queryClient.invalidateQueries({ queryKey: queryKeys.upcomingReleases.all() });
      }
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <LibraryViewTabs activeTab="releases" />
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
            <CalendarClock className="text-primary h-6 w-6" />
            Upcoming Releases ({totalCount})
          </h1>
          <p className="text-muted-foreground text-sm">
            Not-yet-released books from every author and series you follow. Releases stay listed
            even after their date passes, until you remove them.
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          disabled={isRefreshing}
          onClick={() => {
            void handleRefresh();
          }}
        >
          {isRefreshing ? (
            <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
          ) : (
            <RefreshCw className="mr-1.5 h-3.5 w-3.5" />
          )}
          {isRefreshing ? "Checking..." : "Check Now"}
        </Button>
      </div>

      <UpcomingReleasesList
        showSource
        page={page}
        showOverflowHint={false}
        emptyMessage="No upcoming releases tracked yet. Follow an author or series to start tracking."
      />

      {pageCount > 1 && (
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="text-muted-foreground text-xs">
            Showing {currentPage * PAGE_SIZE + 1}–
            {Math.min((currentPage + 1) * PAGE_SIZE, totalCount)} of {totalCount}
          </span>
          <div className="flex items-center gap-2">
            <Button
              size="sm"
              variant="outline"
              disabled={currentPage === 0}
              onClick={() => setPage(currentPage - 1)}
            >
              Previous
            </Button>
            <Button
              size="sm"
              variant="outline"
              disabled={currentPage >= pageCount - 1}
              onClick={() => setPage(currentPage + 1)}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

export default UpcomingReleasesPage;
