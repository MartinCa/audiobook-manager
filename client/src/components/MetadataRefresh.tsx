import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, CalendarDays, CheckCircle2, Loader2, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { PAGE_SIZE } from "@/constants/paging";
import { OperationKeys, SignalREvents } from "@/constants/signalrEvents";
import { OperationProgressBar } from "./OperationProgressBar";
import { metadataRefreshApi } from "@/services/api";
import { useSignalREvent } from "@/hooks/useSignalR";
import { useOperationResync } from "@/hooks/useOperationResync";
import { cutoffDateToUtcIso } from "@/helpers/metadataRefresh";
import { formatDateTime } from "@/helpers/formatHelpers";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
import type { PendingMetadataRefreshListItem } from "@/types/MetadataRefresh";

interface RefreshProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface RefreshCompletePayload {
  totalProcessed: number;
  total: number;
  totalSucceeded: number;
  totalFailed: number;
  stopReason?: string;
}

export function MetadataRefresh() {
  const queryClient = useQueryClient();

  const [refreshing, setRefreshing] = useState(false);
  const [progress, setProgress] = useState<RefreshProgressPayload | null>(null);
  const [cutoffDate, setCutoffDate] = useState("");

  const [page, setPage] = useState(0);

  const { data: pageData, isLoading } = useQuery({
    queryKey: ["metadataRefresh", "pending", page],
    placeholderData: keepPreviousData,
    queryFn: () => metadataRefreshApi.getPendingPage(page, PAGE_SIZE),
  });

  const totalCount = pageData?.total ?? 0;
  const pendingItems = (pageData?.items ?? []) as PendingMetadataRefreshListItem[];
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  // Clamped here rather than only where the pager is drawn, so the page that is *fetched* and the
  // page that is *displayed* can never disagree (same fix as CleanBookUrls' pager).
  const currentPage = Math.min(page, pageCount - 1);

  const goToPage = (next: number) => setPage(next);

  const startBulkMutation = useMutation({
    mutationFn: () => metadataRefreshApi.startBulkRefresh(cutoffDateToUtcIso(cutoffDate)),
    onSuccess: () => {
      toast.success("Metadata refresh started in background");
    },
    onError: (err: unknown) => {
      toast.error(handleApiError(err).message);
    },
  });

  // Invalidate everything that reflects refresh state (the pending list, the book-detail pending
  // banner, the library-list badges) after a completed bulk run. The library-list query folds the
  // pending-summary into its badge computation, so "metadataRefresh"-prefixed invalidation alone
  // would leave stale badges.
  const invalidateRefreshViews = () => {
    void queryClient.invalidateQueries({ queryKey: ["metadataRefresh"] });
    void queryClient.invalidateQueries({ queryKey: ["books"] });
  };

  useSignalREvent<RefreshProgressPayload>(SignalREvents.MetadataRefreshProgress, (data) => {
    setRefreshing(true);
    setProgress(data);
  });

  useSignalREvent<RefreshCompletePayload>(SignalREvents.MetadataRefreshComplete, (data) => {
    setRefreshing(false);
    setProgress(null);
    if (data.stopReason) {
      toast.warning(
        `${data.stopReason}. ${data.totalSucceeded} succeeded, ${data.totalFailed} failed.`,
      );
    } else {
      toast.success(
        `Metadata refresh complete: ${data.totalSucceeded} refreshed, ${data.totalFailed} failed`,
      );
    }
    invalidateRefreshViews();
  });

  // Recover an in-flight bulk refresh (started elsewhere, or events missed while disconnected)
  // on mount and after a SignalR reconnect, the same way LibraryConsistency recovers its check
  // and resolve state.
  useOperationResync(OperationKeys.metadataRefresh, (status) => {
    if (status.isRunning) {
      setRefreshing(true);
      setProgress((prev) =>
        prev && prev.total > 0
          ? prev
          : { processed: status.processed, total: status.total, succeeded: 0, failed: 0 },
      );
    } else {
      setRefreshing(false);
      setProgress(null);
    }
  });

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <Button variant="ghost" size="sm" render={<Link to="/library" />}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </Button>
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <RefreshCw className="text-primary h-6 w-6" />
          Metadata Refresh
        </h1>
        <p className="text-muted-foreground text-sm">
          Re-fetch book metadata from the online source each book was added from, then review the
          changes before they are saved. Books whose source supports it and that are due are
          refreshed in the background.
        </p>
      </div>

      <Card className="space-y-3 p-4">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
          <div className="space-y-1.5">
            <label className="text-muted-foreground text-xs font-semibold uppercase">
              Refresh books never refreshed, or last refreshed before
            </label>
            <div className="flex flex-wrap items-center gap-2">
              <div className="relative">
                <CalendarDays className="text-muted-foreground pointer-events-none absolute top-1/2 left-2.5 h-4 w-4 -translate-y-1/2" />
                <Input
                  type="date"
                  value={cutoffDate}
                  onChange={(e) => setCutoffDate(e.target.value)}
                  className="w-full pl-8 sm:w-44"
                  aria-label="Refresh books last refreshed before this date"
                />
              </div>
              {cutoffDate && (
                <Button
                  variant="link"
                  size="sm"
                  className="h-auto p-0 text-xs"
                  onClick={() => setCutoffDate("")}
                >
                  Clear (never-refreshed only)
                </Button>
              )}
            </div>
            <p className="text-muted-foreground text-xs">
              Leave the date empty to refresh only books that have never been refreshed from an
              online source.
            </p>
          </div>

          <Button
            onClick={() => startBulkMutation.mutate()}
            disabled={refreshing || startBulkMutation.isPending}
            className="w-full sm:w-auto"
          >
            {refreshing || startBulkMutation.isPending ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <RefreshCw className="mr-2 h-4 w-4" />
            )}
            {refreshing ? "Refreshing..." : "Refresh Books"}
          </Button>
        </div>

        {refreshing && progress && (
          <OperationProgressBar
            processed={progress.processed}
            total={progress.total}
            label="Refreshing metadata..."
            subText={`${progress.succeeded} refreshed, ${progress.failed} failed`}
          />
        )}
      </Card>

      <div className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-foreground text-lg font-bold">
            Books with Pending Metadata Changes ({totalCount})
          </h2>
        </div>

        {isLoading ? (
          <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
            <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
            <p className="text-sm">Loading pending changes...</p>
          </div>
        ) : totalCount === 0 ? (
          <Card className="p-12 text-center">
            <CheckCircle2 className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
            <h3 className="text-foreground text-lg font-medium">No pending metadata changes</h3>
            <p className="text-muted-foreground mt-1 text-sm">
              Refreshing a book with differing metadata stores a reviewable snapshot here. Books
              whose metadata already matches their source have nothing pending.
            </p>
          </Card>
        ) : (
          <div className="space-y-2">
            {pendingItems.map((item) => (
              <Link
                key={item.audiobookId}
                to="/library/book/$bookId"
                params={{ bookId: String(item.audiobookId) }}
                className="border-border bg-card hover:bg-muted/50 flex items-center justify-between gap-3 rounded-lg border p-3 transition-colors"
              >
                <div className="min-w-0 flex-1">
                  <div className="text-foreground font-semibold break-words">
                    {item.authors.join(", ")} &mdash; {item.bookName}
                  </div>
                  <div className="text-muted-foreground mt-0.5 text-xs">
                    Pending since {formatDateTime(item.fetchedAt)}
                  </div>
                </div>
                <Badge variant="secondary" className="shrink-0">
                  {item.sourceName}
                </Badge>
              </Link>
            ))}

            {/* Stays rendered even if this page comes back empty while the count is non-zero, so
                the user can page back instead of staring at a dead-end heading. */}
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
                    onClick={() => goToPage(currentPage - 1)}
                  >
                    Previous
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={currentPage >= pageCount - 1}
                    onClick={() => goToPage(currentPage + 1)}
                  >
                    Next
                  </Button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

export default MetadataRefresh;
