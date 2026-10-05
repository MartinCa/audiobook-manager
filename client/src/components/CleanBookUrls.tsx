import { BookQualifierBadges } from "@/components/BookQualifierBadges";
import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Link2, Loader2, Sparkles } from "lucide-react";
import { ActionButton } from "@/components/action-button";
import { Button } from "@/components/ui/button";
import { LinkButton } from "./link-button";
import { Checkbox } from "@/components/ui/checkbox";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import type { PageSizeOption } from "@/constants/paging";
import { OperationKeys, SignalREvents } from "@/constants/signalrEvents";
import { OperationProgressBar } from "./OperationProgressBar";
import { SectionPager } from "./library/SectionPager";
import { urlCleanupApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useSignalREvent } from "@/hooks/useSignalR";
import { useOperationResync } from "@/hooks/useOperationResync";
import { usePageSize } from "@/hooks/usePageSize";
import type { AudiobookUrlCleanup } from "@/types/UrlCleanup";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";

interface ApplyAllProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface ApplyAllCompletePayload {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
  /** True only when the sweep threw out of the background operation: every count is zero then,
   *  indistinguishable from a sweep that had nothing left to do without this flag. */
  errored: boolean;
}

export function CleanBookUrls() {
  const queryClient = useQueryClient();

  // The dirty list is paged server-side: with a few thousand dirty URLs the old unpaged
  // response rendered every book as a card into the DOM and the page became unusably slow.
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePageSize();

  const {
    data: pageData,
    isLoading,
    isFetching,
  } = useQuery({
    queryKey: queryKeys.urlCleanup.page(page, pageSize),
    // keepPreviousData: while the next page loads the previous one stays rendered (with the
    // checkboxes dimmed via isFetching), so the pager doesn't vanish on every navigation.
    placeholderData: keepPreviousData,
    queryFn: () => urlCleanupApi.getDirtyUrlPage(page, pageSize),
  });

  const totalCount = pageData?.totalCount ?? 0;
  const dirtyUrls = (pageData?.items ?? []) as AudiobookUrlCleanup[];

  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));
  // Clamped here rather than only where the pager is drawn, so the page that is *fetched* and the
  // page that is *displayed* can never disagree (same fix as LibraryConsistency's pager).
  const currentPage = Math.min(page, pageCount - 1);

  // null means "not customized yet" - defaults to everything on the loaded page selected. Once
  // the user toggles a box we switch to an explicit set, which is simpler than syncing state off
  // the query result.
  const [customSelection, setCustomSelection] = useState<Set<number> | null>(null);
  const selectedIds = customSelection ?? new Set(dirtyUrls.map((b) => b.audiobookId));

  // The selection is page-scoped: navigating resets it back to "everything on the visible page
  // selected", so the button label always describes what is in front of the user. Hoisted out of
  // the pager buttons so the same reset also covers apply (below).
  const goToPage = (next: number) => {
    setPage(next);
    setCustomSelection(null);
  };

  const handlePageSizeChange = (size: PageSizeOption) => {
    setPageSize(size);
    goToPage(0);
  };

  const applyMutation = useMutation({
    mutationFn: (audiobookIds: number[]) => urlCleanupApi.apply(audiobookIds),
    onSuccess: (result) => {
      notifications.success(
        `Cleaned ${result.updated ?? 0} book URL${result.updated === 1 ? "" : "s"}`,
      );
      // Drop back to page 0 too: whatever page was being looked at may now be a stale slice.
      goToPage(0);
      void queryClient.invalidateQueries({ queryKey: queryKeys.urlCleanup.all() });
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
  });

  // Apply-all is fire-and-forget: the endpoint only starts the background operation, and
  // progress/completion arrive over SignalR - the same flow consistency's bulk resolve uses.
  const [cleaningAll, setCleaningAll] = useState(false);
  const [applyAllProgress, setApplyAllProgress] = useState<ApplyAllProgressPayload | null>(null);

  // Recover an in-flight sweep started elsewhere (or whose events were missed while
  // disconnected) on mount and after a SignalR reconnect, the same way the consistency
  // resolve state is recovered on its page. The returned invalidate is called from the
  // sweep's event handlers so a status response fetched before a real event is discarded
  // instead of clobbering the state the event set.
  const invalidateUrlCleanup = useOperationResync(OperationKeys.urlCleanupApply, (status) => {
    if (status.isRunning) {
      setCleaningAll(true);
      setApplyAllProgress((prev) =>
        prev && prev.total > 0
          ? prev
          : { processed: status.processed, total: status.total, succeeded: 0, failed: 0 },
      );
    } else {
      setCleaningAll(false);
      setApplyAllProgress(null);
    }
  });

  useSignalREvent<ApplyAllProgressPayload>(SignalREvents.UrlCleanupProgress, (data) => {
    invalidateUrlCleanup();
    setCleaningAll(true);
    setApplyAllProgress(data);
  });

  useSignalREvent<ApplyAllCompletePayload>(SignalREvents.UrlCleanupComplete, (data) => {
    invalidateUrlCleanup();
    setCleaningAll(false);
    setApplyAllProgress(null);
    if (data.errored) {
      notifications.error("URL cleanup failed");
    } else if (data.totalFailed > 0) {
      notifications.warning(
        `Cleaned ${data.totalSucceeded} book URL${data.totalSucceeded === 1 ? "" : "s"} (${data.totalFailed} failed)`,
      );
    } else {
      notifications.success(
        `Cleaned ${data.totalSucceeded} book URL${data.totalSucceeded === 1 ? "" : "s"}`,
      );
    }
    // Re-read the authoritative list, whether this tab started the sweep or another did.
    goToPage(0);
    void queryClient.invalidateQueries({ queryKey: queryKeys.urlCleanup.all() });
  });

  const handleApplyAll = async () => {
    try {
      await urlCleanupApi.applyAll();
      notifications.success(`Cleaning all ${totalCount} detected URLs in the background`);
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    }
  };

  const toggleSelected = (id: number) => {
    const next = new Set(selectedIds);
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    setCustomSelection(next);
  };

  const selectAll = () => setCustomSelection(new Set(dirtyUrls.map((b) => b.audiobookId)));
  const clearSelection = () => setCustomSelection(new Set());

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
          <Link2 className="text-primary h-6 w-6" />
          Clean Book URLs
        </h1>
        <p className="text-muted-foreground text-sm">
          Find books whose saved website link carries tracking or session parameters (e.g. an
          Audible <code>ref=</code>/<code>pf_rd_*</code> tag) and strip them down to the clean,
          canonical URL. Newly fetched metadata is already saved clean — this is for links saved
          before that.
        </p>
      </div>

      {cleaningAll && applyAllProgress && (
        <OperationProgressBar
          processed={applyAllProgress.processed}
          total={applyAllProgress.total}
          label={`Cleaning all detected URLs (${applyAllProgress.succeeded} cleaned, ${applyAllProgress.failed} failed)`}
        />
      )}

      <div className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-foreground text-lg font-bold">
            Books with Trackable URLs ({totalCount})
          </h2>
          {totalCount > 0 && (
            <div className="flex flex-wrap items-center gap-2">
              <Button variant="link" size="sm" className="h-auto p-0 text-xs" onClick={selectAll}>
                Select all
              </Button>
              <Button
                variant="link"
                size="sm"
                className="h-auto p-0 text-xs"
                onClick={clearSelection}
              >
                Clear
              </Button>
              <ActionButton
                size="sm"
                variant="default"
                icon={Sparkles}
                status={applyMutation.status}
                disabled={selectedIds.size === 0}
                onClick={() => applyMutation.mutate(Array.from(selectedIds))}
              >
                Clean {selectedIds.size} URL{selectedIds.size === 1 ? "" : "s"}
              </ActionButton>
              <Button
                size="sm"
                variant="outline"
                disabled={cleaningAll}
                onClick={() => void handleApplyAll()}
              >
                {cleaningAll ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <Sparkles className="mr-2 h-4 w-4" />
                )}
                Clean all detected URLs
              </Button>
            </div>
          )}
        </div>

        {isLoading ? (
          <div role="status" aria-label="Scanning saved URLs..." className="space-y-2">
            {Array.from({ length: 6 }, (_, i) => (
              <div
                key={i}
                className="border-border bg-card flex items-start gap-3 rounded-lg border p-3"
              >
                <Skeleton className="mt-1 size-4 shrink-0 rounded-[4px]" />
                <div className="min-w-0 flex-1 space-y-2">
                  <Skeleton className="h-4 w-2/5 max-w-80" />
                  <Skeleton className="h-3 w-4/5 max-w-120" />
                  <Skeleton className="h-3 w-3/5 max-w-96" />
                </div>
              </div>
            ))}
          </div>
        ) : totalCount === 0 ? (
          <Card className="p-12 text-center">
            <Link2 className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
            <h3 className="text-foreground text-lg font-medium">No trackable URLs found</h3>
            <p className="text-muted-foreground mt-1 text-sm">
              Every saved book URL in your library is already clean.
            </p>
          </Card>
        ) : (
          <div className="space-y-2">
            {dirtyUrls.map((b) => (
              <div
                key={b.audiobookId}
                className="border-border bg-card flex items-start gap-3 rounded-lg border p-3"
              >
                <Checkbox
                  className="mt-1"
                  checked={selectedIds.has(b.audiobookId)}
                  onCheckedChange={() => toggleSelected(b.audiobookId)}
                  disabled={isFetching || applyMutation.isPending}
                />
                <div className="min-w-0 flex-1">
                  <Link
                    to="/library/book/$bookId"
                    params={{ bookId: String(b.audiobookId) }}
                    className="text-foreground font-semibold break-words hover:underline"
                  >
                    {b.authors.join(", ")} &mdash; {b.bookName}
                  </Link>
                  <BookQualifierBadges qualifiers={b.qualifiers} className="ml-2" />
                  <div className="mt-1 space-y-0.5 text-xs">
                    <div className="text-muted-foreground break-all">
                      <span className="line-through decoration-red-500/60">{b.currentUrl}</span>
                    </div>
                    <div className="break-all text-emerald-600 dark:text-emerald-400">
                      {b.cleanedUrl}
                    </div>
                  </div>
                </div>
              </div>
            ))}

            {/* Stays rendered even if this page comes back empty while the count is non-zero
                (someone cleaned rows from another tab, or the clamp lagged a shrinking total),
                so the user can page back instead of staring at a dead-end heading. */}
            <SectionPager
              currentPage={currentPage}
              pageCount={pageCount}
              totalCount={totalCount}
              pageSize={pageSize}
              onPageChange={goToPage}
              onPageSizeChange={handlePageSizeChange}
            />
          </div>
        )}
      </div>
    </div>
  );
}

export default CleanBookUrls;
