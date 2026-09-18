import { useState, useEffect } from "react";
import { useNavigate } from "@tanstack/react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { BookMarked, Search, X, RefreshCw, Loader2, Layers, Sparkles } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { PAGE_SIZE } from "@/constants/paging";
import { OperationKeys, SignalREvents } from "@/constants/signalrEvents";
import { LibraryViewTabs } from "./LibraryViewTabs";
import { OperationProgressBar } from "@/components/OperationProgressBar";
import { SeriesMatchDialog } from "./SeriesMatchDialog";
import { SeriesListEntry } from "./SeriesListEntry";
import { seriesApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useSignalREvent } from "@/hooks/useSignalR";
import { useOperationResync } from "@/hooks/useOperationResync";
import { useClampedPage } from "@/hooks/useClampedPage";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import { Route } from "@/routes/library/series/index";
import type { SeriesOverview } from "@/types/Series";

interface SeriesRefreshProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface SeriesRefreshCompletePayload {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
  stopReason?: string;
}

export function SeriesOverviewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { q = "" } = Route.useSearch();
  const [prevQ, setPrevQ] = useState(q);
  const [filter, setFilter] = useState(q);
  // Page is internal state rather than a route param: like CleanBookUrls, the list renders one
  // page at a time and the pager clamps it; a filter change drops back to page 0.
  const [page, setPage] = useState(0);

  if (prevQ !== q) {
    setPrevQ(q);
    if (filter.trim() !== q) {
      setFilter(q);
    }
  }

  useEffect(() => {
    const timer = setTimeout(() => {
      const trimmed = filter.trim();
      if (trimmed !== q) {
        setPage(0);
        void navigate({
          to: "/library/series",
          search: (prev) => ({
            ...prev,
            q: trimmed || undefined,
          }),
          replace: true,
        });
      }
    }, 300);
    return () => clearTimeout(timer);
  }, [filter, q, navigate]);

  const handleClearFilter = () => {
    setFilter("");
    setPage(0);
    if (q) {
      void navigate({
        to: "/library/series",
        search: (prev) => ({
          ...prev,
          q: undefined,
        }),
        replace: true,
      });
    }
  };

  const [refreshing, setRefreshing] = useState(false);
  const [refreshProgress, setRefreshProgress] = useState<SeriesRefreshProgressPayload | null>(null);
  const [matchDialogOpen, setMatchDialogOpen] = useState(false);

  // The header badges need the whole-library counts; the page itself is one slice. The counts
  // are cheap and long-lived, so they are cached separately and only invalidated by the match/
  // refresh flows that change them.
  const { data: counts } = useQuery({
    queryKey: queryKeys.seriesCounts(),
    queryFn: () => seriesApi.getSeriesCounts(),
    staleTime: 30_000,
  });

  const {
    data: pageData,
    isLoading: loading,
    refetch,
  } = useQuery({
    // The page cursor is only clamped on display (below) - the total that sizes the pager comes
    // from this very response, so the fetch cannot know in advance that the cursor outran a list
    // that shrank. The pager stays rendered even when such a page comes back empty (its items
    // count on totalCount, not on the items), so the user can page back instead of staring at a
    // dead-end heading - the CleanBookUrls shape.
    queryKey: queryKeys.series.page(q, page),
    // keepPreviousData: while the next page loads the previous one stays rendered, so the pager
    // doesn't vanish on every navigation.
    placeholderData: keepPreviousData,
    queryFn: () => seriesApi.getSeriesPage(page, PAGE_SIZE, q),
  });

  const seriesList = (pageData?.items ?? []) as SeriesOverview[];
  const totalCount = pageData?.totalCount ?? 0;

  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  // Clamped here rather than only where the pager is drawn, so the page that is *fetched* and the
  // page that is *displayed* can never disagree (same fix as LibraryConsistency's pager).
  const currentPage = Math.min(page, pageCount - 1);

  // A refresh-all or a bulk match can shrink the list while the user sits on a later page; pull
  // the raw page back into range so the next fetch lands on a valid page rather than coming back
  // empty (the pager here stays rendered even for an empty page, and the clamp finishes the job).
  useClampedPage(page, pageCount, setPage);

  useSignalREvent<SeriesRefreshProgressPayload>(SignalREvents.SeriesRefreshProgress, (data) => {
    setRefreshing(true);
    setRefreshProgress(data);
  });

  useSignalREvent<SeriesRefreshCompletePayload>(SignalREvents.SeriesRefreshComplete, (arg) => {
    setRefreshing(false);
    setRefreshProgress(null);
    const msg = arg.stopReason
      ? `Refresh stopped after ${arg.totalProcessed} series: ${arg.stopReason}`
      : `Refresh complete: ${arg.totalSucceeded} of ${arg.totalProcessed} series updated${
          arg.totalFailed > 0 ? ` (${arg.totalFailed} failed)` : ""
        }`;
    notifications.success(msg);
    // A refresh can match previously-unmatched series - i.e. shrink the list. Drop back to page
    // 0 so the refetch below never asks for a page the smaller list no longer has.
    setPage(0);
    void queryClient.invalidateQueries({ queryKey: queryKeys.series.all() });
    void queryClient.invalidateQueries({ queryKey: queryKeys.seriesCounts() });
  });

  // Recover an in-flight refresh-all (started elsewhere, or events missed while disconnected) on
  // mount and after a SignalR reconnect, the same way MetadataRefresh recovers its bulk series
  // refresh: a page opened mid-refresh must show the running bar instead of looking idle until
  // the next event. The completion toast belongs to the SeriesRefreshComplete event only.
  useOperationResync(OperationKeys.seriesRefresh, (status) => {
    if (status.isRunning) {
      setRefreshing(true);
      setRefreshProgress((prev) =>
        prev && prev.total > 0
          ? prev
          : { processed: status.processed, total: status.total, succeeded: 0, failed: 0 },
      );
    } else {
      setRefreshing(false);
      setRefreshProgress(null);
    }
  });

  const handleRefreshAll = async () => {
    setRefreshing(true);
    try {
      await seriesApi.startRefreshAll();
      notifications.success("Refreshing all series in background");
    } catch (err: unknown) {
      setRefreshing(false);
      notifications.error(handleApiError(err).message);
    }
  };

  const unmatchedCount = counts?.unmatched ?? 0;
  const matchedCount = counts?.matched ?? 0;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <LibraryViewTabs activeTab="series" />

        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              void refetch();
            }}
            disabled={loading || refreshing}
          >
            <RefreshCw className={`mr-2 h-4 w-4 ${loading ? "animate-spin" : ""}`} />
            Reload
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => setMatchDialogOpen(true)}
            disabled={loading || refreshing || unmatchedCount === 0}
          >
            <Sparkles className="mr-2 h-4 w-4" />
            Bulk Match ({unmatchedCount})
          </Button>

          <Button
            variant="default"
            size="sm"
            onClick={() => {
              void handleRefreshAll();
            }}
            disabled={loading || refreshing || matchedCount === 0}
          >
            <Layers className="mr-2 h-4 w-4" />
            {refreshing ? "Refreshing All..." : "Refresh All Series"}
          </Button>
        </div>
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <BookMarked className="text-primary h-6 w-6" />
          Series ({counts?.total ?? totalCount})
        </h1>
        <p className="text-muted-foreground text-sm">
          Every series in your library. Match series to metadata providers to identify missing parts
          and maintain reading orders.
        </p>
      </div>

      {refreshing && refreshProgress && (
        <OperationProgressBar
          processed={refreshProgress.processed}
          total={refreshProgress.total}
          label={`Refreshing series metadata (${refreshProgress.succeeded} succeeded, ${refreshProgress.failed} failed)`}
        />
      )}

      <div className="relative max-w-md">
        <Search className="text-muted-foreground absolute top-2.5 left-3 h-4 w-4" />
        <Input
          placeholder="Filter series or authors..."
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              const trimmed = filter.trim();
              if (trimmed !== q) {
                setPage(0);
                void navigate({
                  to: "/library/series",
                  search: (prev) => ({
                    ...prev,
                    q: trimmed || undefined,
                  }),
                  replace: true,
                });
              }
            }
          }}
          className="pr-9 pl-9"
        />
        {filter ? (
          <button
            type="button"
            onClick={handleClearFilter}
            aria-label="Clear filter"
            className="text-muted-foreground hover:text-foreground absolute top-2.5 right-2.5 cursor-pointer rounded-sm p-0.5 transition-colors"
          >
            <X className="h-4 w-4" />
          </button>
        ) : null}
      </div>

      {loading && seriesList.length === 0 ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Loading series...</p>
        </div>
      ) : totalCount === 0 ? (
        <Card className="p-12 text-center">
          <BookMarked className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
          <h3 className="text-foreground text-lg font-medium">No series found</h3>
          <p className="text-muted-foreground mt-1 text-sm">
            {q.trim()
              ? "No series match your search filter."
              : "No audiobooks with series tags have been organized yet."}
          </p>
        </Card>
      ) : (
        <div className="space-y-2">
          {seriesList.map((s) => (
            <SeriesListEntry key={s.name} series={s} />
          ))}

          {/* Stays rendered even if this page comes back empty while the count is non-zero, so the
              user can page back instead of staring at a dead-end heading. Same shape as CleanBookUrls. */}
          {pageCount > 1 && (
            <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
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
      )}

      <SeriesMatchDialog
        open={matchDialogOpen}
        onOpenChange={setMatchDialogOpen}
        onMatched={() => {
          // Matching is the other shrink path (unmatched series disappear from the list).
          setPage(0);
          void queryClient.invalidateQueries({ queryKey: queryKeys.series.all() });
          void queryClient.invalidateQueries({ queryKey: queryKeys.seriesCounts() });
        }}
      />
    </div>
  );
}

export default SeriesOverviewPage;
