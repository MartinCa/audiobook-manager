import { useState } from "react";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { PAGE_SIZE } from "@/constants/paging";
import { LastRefreshedHint } from "@/components/LastRefreshedHint";
import { SeriesRefreshPendingDialog } from "@/components/library/SeriesRefreshPendingDialog";
import { seriesApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import type { SeriesRefreshPendingListItem } from "@/types/SeriesRefresh";

/**
 * The "series with pending refresh changes" section of the /library/metadata-refresh page:
 * one page of the server-side paged pending list (rows exist only for series whose last refresh
 * found explicit changes), each row opening the shared review dialog. The count badge comes
 * from a cheap count endpoint, never from fetching the full list.
 */
export function SeriesRefreshPendingList() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(0);
  const [reviewing, setReviewing] = useState<SeriesRefreshPendingListItem | null>(null);

  const { data: pageData, isLoading } = useQuery({
    queryKey: queryKeys.seriesPending.page(page),
    placeholderData: keepPreviousData,
    queryFn: () => seriesApi.getSeriesPendingPage(page, PAGE_SIZE),
  });

  const { data: totalCount } = useQuery({
    queryKey: queryKeys.seriesPending.count(),
    queryFn: () => seriesApi.getSeriesPendingCount(),
  });

  // An apply or dismiss changes which rows exist; the shared dialog invalidates its own
  // queries, and the section re-fetches with them.
  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.seriesPending.all() });
  };

  // Clamped here rather than only where the pager is drawn, so the page that is *fetched* and the
  // page that is *displayed* can never disagree (same fix as CleanBookUrls/MetadataRefresh).
  const count = totalCount ?? pageData?.total ?? 0;
  const items = (pageData?.items ?? []) as SeriesRefreshPendingListItem[];
  const pageCount = Math.max(1, Math.ceil(count / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount - 1);

  return (
    <div className="space-y-3">
      <h2 className="text-foreground text-lg font-bold">
        Series with Pending Refresh Changes ({count})
      </h2>

      {isLoading ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-12">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Loading pending changes...</p>
        </div>
      ) : count === 0 ? (
        <Card className="p-10 text-center">
          <CheckCircle2 className="text-muted-foreground/40 mx-auto mb-3 h-10 w-10" />
          <h3 className="text-foreground text-base font-medium">No series have pending changes</h3>
          <p className="text-muted-foreground mt-1 text-sm">
            Refreshing a matched series whose source roster moved books, renamed parts or dropped
            entries stores a reviewable snapshot here. No-change refreshes have nothing pending.
          </p>
        </Card>
      ) : (
        <div className="space-y-2">
          {items.map((item) => (
            <div
              key={item.seriesName}
              className="border-border bg-card hover:bg-muted/50 flex flex-col justify-between gap-3 rounded-lg border p-3 transition-colors sm:flex-row sm:items-center"
            >
              <div className="min-w-0 flex-1">
                <div className="text-foreground font-semibold break-words">
                  {item.seriesName}
                  {item.sourceSeriesName && item.sourceSeriesName.trim() !== item.seriesName ? (
                    <span className="text-muted-foreground font-normal">
                      {" "}
                      (source: {item.sourceSeriesName})
                    </span>
                  ) : null}
                </div>
                <div className="text-muted-foreground mt-0.5 text-xs">
                  <LastRefreshedHint lastRefreshedAt={item.fetchedAt} /> · {item.changeCount} change
                  {item.changeCount === 1 ? "" : "s"}
                </div>
              </div>
              <div className="flex shrink-0 items-center gap-2 self-end sm:self-center">
                <Badge variant="secondary" className="shrink-0">
                  {item.sourceName}
                </Badge>
                <Button size="sm" className="h-7 text-xs" onClick={() => setReviewing(item)}>
                  Review
                </Button>
              </div>
            </div>
          ))}

          {/* Stays rendered even if this page comes back empty while the count is non-zero, so
              the user can page back instead of staring at a dead-end heading. */}
          {pageCount > 1 && (
            <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
              <span className="text-muted-foreground text-xs">
                Showing {currentPage * PAGE_SIZE + 1}–
                {Math.min((currentPage + 1) * PAGE_SIZE, count)} of {count}
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

      <SeriesRefreshPendingDialog
        open={reviewing !== null}
        onOpenChange={(open) => {
          if (!open) {
            setReviewing(null);
            invalidate();
          }
        }}
        seriesName={reviewing?.seriesName ?? ""}
      />
    </div>
  );
}

export default SeriesRefreshPendingList;
