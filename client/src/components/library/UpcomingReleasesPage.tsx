import { useState } from "react";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, Loader2, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PAGE_SIZE } from "@/constants/paging";
import { LibraryViewTabs } from "./LibraryViewTabs";
import { UpcomingReleasesList } from "./UpcomingReleasesList";
import { upcomingReleasesApi } from "@/services/api";
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
  const [refreshing, setRefreshing] = useState(false);

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

  const handleRefresh = async () => {
    setRefreshing(true);
    try {
      await upcomingReleasesApi.refreshUpcomingReleases();
      notifications.success("Checked followed authors and series for new releases");
      void queryClient.invalidateQueries({ queryKey: queryKeys.upcomingReleases.all() });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setRefreshing(false);
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
          disabled={refreshing}
          onClick={() => {
            void handleRefresh();
          }}
        >
          {refreshing ? (
            <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
          ) : (
            <RefreshCw className="mr-1.5 h-3.5 w-3.5" />
          )}
          Check Now
        </Button>
      </div>

      <UpcomingReleasesList
        showSource
        page={page}
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
