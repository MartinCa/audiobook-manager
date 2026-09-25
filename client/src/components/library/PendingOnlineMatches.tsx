import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, ChevronDown, ChevronRight, Loader2, Search, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { LinkButton } from "../LinkButton";
import { PendingOnlineMatchRowPanel } from "./PendingOnlineMatchRowPanel";
import { pendingOnlineMatchApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { PAGE_SIZE } from "@/constants/paging";
import { formatDateTime } from "@/helpers/formatHelpers";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type { PendingOnlineMatchListItem } from "@/types/PendingOnlineMatch";

/**
 * The two-list page a bulk "Search Online Metadata" run is processed from: Pending (a book still
 * awaiting a selection or a reject) and Failed/Rejected (a book the user rejected every candidate
 * for). Both lists reuse the same book-row shell; only their per-row actions differ.
 */
export function PendingOnlineMatches() {
  const queryClient = useQueryClient();
  const [pendingPage, setPendingPage] = useState(0);
  const [failedPage, setFailedPage] = useState(0);
  const [expandedId, setExpandedId] = useState<number | null>(null);
  const [rejectingId, setRejectingId] = useState<number | null>(null);
  const [dismissingId, setDismissingId] = useState<number | null>(null);

  const { data: pendingData, isLoading: pendingLoading } = useQuery({
    queryKey: queryKeys.pendingOnlineMatch.pendingPage(pendingPage),
    placeholderData: keepPreviousData,
    queryFn: () => pendingOnlineMatchApi.getPendingPage(pendingPage, PAGE_SIZE),
  });

  const { data: failedData, isLoading: failedLoading } = useQuery({
    queryKey: queryKeys.pendingOnlineMatch.failedPage(failedPage),
    placeholderData: keepPreviousData,
    queryFn: () => pendingOnlineMatchApi.getFailedPage(failedPage, PAGE_SIZE),
  });

  const pendingItems = (pendingData?.items ?? []) as PendingOnlineMatchListItem[];
  const pendingTotal = pendingData?.total ?? 0;
  const pendingPageCount = Math.max(1, Math.ceil(pendingTotal / PAGE_SIZE));
  const currentPendingPage = Math.min(pendingPage, pendingPageCount - 1);

  const failedItems = (failedData?.items ?? []) as PendingOnlineMatchListItem[];
  const failedTotal = failedData?.total ?? 0;
  const failedPageCount = Math.max(1, Math.ceil(failedTotal / PAGE_SIZE));
  const currentFailedPage = Math.min(failedPage, failedPageCount - 1);

  const invalidateViews = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.pendingOnlineMatch.all() });
  };

  const handleReject = async (id: number) => {
    setRejectingId(id);
    try {
      await pendingOnlineMatchApi.reject(id);
      notifications.success("Moved to Failed/Rejected");
      if (expandedId === id) setExpandedId(null);
      invalidateViews();
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setRejectingId(null);
    }
  };

  const handleDismiss = async (id: number) => {
    setDismissingId(id);
    try {
      await pendingOnlineMatchApi.dismiss(id);
      notifications.success("Dismissed");
      invalidateViews();
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setDismissingId(null);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <LinkButton variant="ghost" size="sm" render={<Link to="/library" />}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </LinkButton>
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <Search className="text-primary h-6 w-6" />
          Pending Online Matches
        </h1>
        <p className="text-muted-foreground text-sm">
          Results from a bulk "Search Online Metadata" run. Expand a pending book to pick a match,
          or reject it to move it to the Failed/Rejected list below.
        </p>
      </div>

      <div className="space-y-3">
        <h2 className="text-foreground text-lg font-bold">Pending ({pendingTotal})</h2>

        {pendingLoading ? (
          <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
            <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
            <p className="text-sm">Loading pending matches...</p>
          </div>
        ) : pendingTotal === 0 ? (
          <Card className="p-8 text-center">
            <p className="text-muted-foreground text-sm">
              No pending online matches. Start a bulk search from the library's selection toolbar.
            </p>
          </Card>
        ) : (
          <div className="space-y-2">
            {pendingItems.map((item) => {
              const isExpanded = expandedId === item.audiobookId;
              return (
                <div
                  key={item.audiobookId}
                  className="border-border bg-card overflow-hidden rounded-lg border"
                >
                  <div className="flex items-center gap-3 p-3">
                    <button
                      type="button"
                      className="flex min-w-0 flex-1 items-center gap-2 text-left"
                      onClick={() => setExpandedId(isExpanded ? null : item.audiobookId)}
                    >
                      {isExpanded ? (
                        <ChevronDown className="text-muted-foreground h-4 w-4 shrink-0" />
                      ) : (
                        <ChevronRight className="text-muted-foreground h-4 w-4 shrink-0" />
                      )}
                      <div className="min-w-0 flex-1">
                        <div className="text-foreground font-semibold break-words">
                          {item.authors.join(", ")} &mdash; {item.bookName}
                        </div>
                        <div className="text-muted-foreground mt-0.5 text-xs">
                          Searched {formatDateTime(item.searchedAt)} · {item.results.length}{" "}
                          {item.results.length === 1 ? "result" : "results"}
                        </div>
                        <div className="mt-1.5 flex flex-wrap gap-1">
                          {item.sourceNames.map((source) => (
                            <Badge key={source} variant="outline" className="text-[10px]">
                              {source}
                            </Badge>
                          ))}
                        </div>
                      </div>
                    </button>
                    <LinkButton
                      variant="ghost"
                      size="sm"
                      render={
                        <Link
                          to="/library/book/$bookId"
                          params={{ bookId: String(item.audiobookId) }}
                        />
                      }
                    >
                      View
                    </LinkButton>
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={rejectingId === item.audiobookId}
                      onClick={() => {
                        void handleReject(item.audiobookId);
                      }}
                    >
                      {rejectingId === item.audiobookId ? (
                        <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
                      ) : (
                        <X className="mr-1.5 h-3.5 w-3.5" />
                      )}
                      Reject
                    </Button>
                  </div>

                  {isExpanded && (
                    <div className="border-border bg-muted/20 border-t p-3">
                      <PendingOnlineMatchRowPanel
                        item={item}
                        onResolved={() => setExpandedId(null)}
                      />
                    </div>
                  )}
                </div>
              );
            })}

            {pendingPageCount > 1 && (
              <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
                <span className="text-muted-foreground text-xs">
                  Showing {currentPendingPage * PAGE_SIZE + 1}–
                  {Math.min((currentPendingPage + 1) * PAGE_SIZE, pendingTotal)} of {pendingTotal}
                </span>
                <div className="flex items-center gap-2">
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={currentPendingPage === 0}
                    onClick={() => setPendingPage(currentPendingPage - 1)}
                  >
                    Previous
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={currentPendingPage >= pendingPageCount - 1}
                    onClick={() => setPendingPage(currentPendingPage + 1)}
                  >
                    Next
                  </Button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      <div className="space-y-3">
        <h2 className="text-foreground text-lg font-bold">Failed / Rejected ({failedTotal})</h2>

        {failedLoading ? (
          <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
            <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
            <p className="text-sm">Loading failed/rejected matches...</p>
          </div>
        ) : failedTotal === 0 ? (
          <Card className="p-8 text-center">
            <p className="text-muted-foreground text-sm">No failed or rejected matches.</p>
          </Card>
        ) : (
          <div className="space-y-2">
            {failedItems.map((item) => (
              <div
                key={item.audiobookId}
                className="border-border bg-card flex items-center gap-3 rounded-lg border p-3"
              >
                <div className="min-w-0 flex-1">
                  <div className="text-foreground font-semibold break-words">
                    {item.authors.join(", ")} &mdash; {item.bookName}
                  </div>
                  <div className="text-muted-foreground mt-0.5 text-xs">
                    Searched {formatDateTime(item.searchedAt)}
                  </div>
                </div>
                <LinkButton
                  variant="ghost"
                  size="sm"
                  render={
                    <Link
                      to="/library/book/$bookId"
                      params={{ bookId: String(item.audiobookId) }}
                    />
                  }
                >
                  View
                </LinkButton>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={dismissingId === item.audiobookId}
                  onClick={() => {
                    void handleDismiss(item.audiobookId);
                  }}
                >
                  {dismissingId === item.audiobookId ? (
                    <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
                  ) : null}
                  Dismiss
                </Button>
              </div>
            ))}

            {failedPageCount > 1 && (
              <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
                <span className="text-muted-foreground text-xs">
                  Showing {currentFailedPage * PAGE_SIZE + 1}–
                  {Math.min((currentFailedPage + 1) * PAGE_SIZE, failedTotal)} of {failedTotal}
                </span>
                <div className="flex items-center gap-2">
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={currentFailedPage === 0}
                    onClick={() => setFailedPage(currentFailedPage - 1)}
                  >
                    Previous
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={currentFailedPage >= failedPageCount - 1}
                    onClick={() => setFailedPage(currentFailedPage + 1)}
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

export default PendingOnlineMatches;
