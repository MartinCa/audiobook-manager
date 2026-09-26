import { useMemo, useState } from "react";
import { Link } from "@tanstack/react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft,
  CalendarDays,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Filter,
  ListRestart,
  Loader2,
  RefreshCw,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { PAGE_SIZE } from "@/constants/paging";
import { OperationKeys, SignalREvents } from "@/constants/signalrEvents";
import { LinkButton } from "./LinkButton";
import { OperationProgressBar } from "./OperationProgressBar";
import { SeriesRefreshPendingList } from "./library/SeriesRefreshPendingList";
import { SeriesConsistencyIssueList } from "./library/SeriesConsistencyIssueList";
import { AuthorConsistencyIssueList } from "./library/AuthorConsistencyIssueList";
import { PendingRefreshRowPanel } from "./library/PendingRefreshRowPanel";
import { metadataRefreshApi, metadataSearchApi, seriesApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useSignalREvent } from "@/hooks/useSignalR";
import { useOperationResync } from "@/hooks/useOperationResync";
import { cutoffDateToUtcIso } from "@/helpers/metadataRefresh";
import { formatDateTime } from "@/helpers/formatHelpers";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import { METADATA_REFRESH_FIELDS, METADATA_REFRESH_FIELD_LABELS } from "@/types/MetadataRefresh";
import type { PendingMetadataRefreshListItem } from "@/types/MetadataRefresh";

interface ApplyProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

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

  const [refreshingSeries, setRefreshingSeries] = useState(false);
  const [seriesProgress, setSeriesProgress] = useState<RefreshProgressPayload | null>(null);

  const [page, setPage] = useState(0);
  const [fieldFilter, setFieldFilter] = useState<Set<string>>(() => new Set());
  const fieldFilterList = useMemo(() => Array.from(fieldFilter), [fieldFilter]);
  const [sourceFilter, setSourceFilter] = useState<Set<string>>(() => new Set());
  const sourceFilterList = useMemo(() => Array.from(sourceFilter), [sourceFilter]);
  const hasActiveFilter = fieldFilter.size > 0 || sourceFilter.size > 0;
  const [selectedIds, setSelectedIds] = useState<Set<number>>(() => new Set());
  const [expandedId, setExpandedId] = useState<number | null>(null);

  const [applying, setApplying] = useState(false);
  const [applyProgress, setApplyProgress] = useState<ApplyProgressPayload | null>(null);
  const [dismissing, setDismissing] = useState(false);

  // No hardcoded source list on the frontend (AGENTS.md's "Adding a metadata source scraper"
  // invariant) - the source filter dropdown's options come from the same registered-scrapers
  // endpoint the search dialog's source picker uses.
  const { data: services = [] } = useQuery({
    queryKey: queryKeys.metadataServices(),
    queryFn: () => metadataSearchApi.getServices(),
  });

  const { data: pageData, isLoading } = useQuery({
    queryKey: queryKeys.metadataRefresh.pendingPage(page, fieldFilterList, sourceFilterList),
    placeholderData: keepPreviousData,
    queryFn: () =>
      metadataRefreshApi.getPendingPage(page, PAGE_SIZE, fieldFilterList, sourceFilterList),
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
      notifications.success("Metadata refresh started in background");
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
  });

  // Invalidate everything that reflects refresh state (the pending list, the book-detail pending
  // banner, the library-list badges) after a completed bulk run. The library-list query folds the
  // pending-summary into its badge computation, so "metadataRefresh"-prefixed invalidation alone
  // would leave stale badges.
  const invalidateRefreshViews = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.metadataRefresh.all() });
    void queryClient.invalidateQueries({ queryKey: queryKeys.books.all() });
  };

  // Recover an in-flight bulk apply (selected books, or every book matching a filter) the same
  // way the bulk refresh above recovers.
  const invalidateMetadataApply = useOperationResync(OperationKeys.metadataApply, (status) => {
    if (status.isRunning) {
      setApplying(true);
      setApplyProgress((prev) =>
        prev && prev.total > 0
          ? prev
          : { processed: status.processed, total: status.total, succeeded: 0, failed: 0 },
      );
    } else {
      setApplying(false);
      setApplyProgress(null);
    }
  });

  useSignalREvent<ApplyProgressPayload>(SignalREvents.MetadataApplyProgress, (data) => {
    invalidateMetadataApply();
    setApplying(true);
    setApplyProgress(data);
  });

  useSignalREvent<ApplyProgressPayload>(SignalREvents.MetadataApplyComplete, (data) => {
    invalidateMetadataApply();
    setApplying(false);
    setApplyProgress(null);
    notifications.success(`Apply complete: ${data.succeeded} applied, ${data.failed} failed`);
    setSelectedIds(new Set());
    invalidateRefreshViews();
  });

  const applySelectedMutation = useMutation({
    mutationFn: () => metadataRefreshApi.applySelected(Array.from(selectedIds)),
    onSuccess: () => {
      notifications.success("Applying selected books' metadata changes in background");
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
  });

  const applyFilteredMutation = useMutation({
    mutationFn: () => metadataRefreshApi.applyFiltered(fieldFilterList),
    onSuccess: () => {
      notifications.success("Applying every book matching the filter in background");
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
  });

  // Synchronous (a pure DB delete, no scraper/file work), unlike apply-selected: no SignalR
  // progress needed. Re-reads the authoritative list afterward rather than optimistically
  // removing rows client-side, the same "don't outrun what the server reported" rule the
  // consistency bulk-resolve follows.
  const dismissSelectedMutation = useMutation({
    mutationFn: () => metadataRefreshApi.dismissSelected(Array.from(selectedIds)),
    onMutate: () => setDismissing(true),
    onSuccess: (result) => {
      notifications.success(`Dismissed ${result.dismissed} pending change(s)`);
      setSelectedIds(new Set());
      invalidateRefreshViews();
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
    onSettled: () => setDismissing(false),
  });

  // Synchronous (no scraper calls, so no SignalR progress needed): re-diffs every pending row
  // against the library, series mapping patterns, and changed-fields logic as they stand right
  // now. Reflects a mapping pattern (or any other setting) added after a snapshot was captured
  // without waiting for the book's next scheduled refresh.
  const reevaluateMutation = useMutation({
    mutationFn: () => metadataRefreshApi.reevaluatePending(),
    onSuccess: (result) => {
      notifications.success(
        `Re-evaluated ${result.processed} pending change(s): ${result.updated} updated, ${result.removed} resolved`,
      );
      invalidateRefreshViews();
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
  });

  const toggleFieldFilter = (field: string) => {
    setFieldFilter((prev) => {
      const next = new Set(prev);
      if (next.has(field)) next.delete(field);
      else next.add(field);
      return next;
    });
    setPage(0);
  };

  const toggleSourceFilter = (source: string) => {
    setSourceFilter((prev) => {
      const next = new Set(prev);
      if (next.has(source)) next.delete(source);
      else next.add(source);
      return next;
    });
    setPage(0);
  };

  const toggleSelected = (id: number) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const pageIds = pendingItems.map((item) => item.audiobookId);
  const allOnPageSelected = pageIds.length > 0 && pageIds.every((id) => selectedIds.has(id));

  const toggleSelectAllOnPage = () => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (allOnPageSelected) {
        for (const id of pageIds) next.delete(id);
      } else {
        for (const id of pageIds) next.add(id);
      }
      return next;
    });
  };

  // Recover an in-flight bulk refresh (started elsewhere, or events missed while disconnected)
  // on mount and after a SignalR reconnect, the same way LibraryConsistency recovers its check
  // and resolve state. The returned invalidate is called from the refresh's event handlers so a
  // status response fetched before a real event is discarded instead of clobbering the state
  // the event set.
  const invalidateMetadataRefresh = useOperationResync(OperationKeys.metadataRefresh, (status) => {
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

  // The bulk series refresh mirrors the book sweep: fire-and-forget, SignalR progress, and the
  // same operation-status recovery. Completion invalidates the pending list (only series whose
  // refresh found changes appear in it).
  const startBulkSeriesRefresh = async () => {
    setRefreshingSeries(true);
    try {
      await seriesApi.startRefreshAll();
    } catch (err: unknown) {
      setRefreshingSeries(false);
      notifications.error(handleApiError(err).message);
    }
  };

  useSignalREvent<RefreshProgressPayload>(SignalREvents.MetadataRefreshProgress, (data) => {
    invalidateMetadataRefresh();
    setRefreshing(true);
    setProgress(data);
  });

  useSignalREvent<RefreshCompletePayload>(SignalREvents.MetadataRefreshComplete, (data) => {
    invalidateMetadataRefresh();
    setRefreshing(false);
    setProgress(null);
    if (data.stopReason) {
      notifications.warning(
        `${data.stopReason}. ${data.totalSucceeded} succeeded, ${data.totalFailed} failed.`,
      );
    } else {
      notifications.success(
        `Metadata refresh complete: ${data.totalSucceeded} refreshed, ${data.totalFailed} failed`,
      );
    }
    invalidateRefreshViews();
  });

  // Recover an in-flight bulk series refresh (started elsewhere, or events missed while
  // disconnected) on mount and after a SignalR reconnect, the same way the book sweep above
  // recovers. Completion invalidates the pending list (only series whose refresh found changes
  // appear in it). The returned invalidate is called from the refresh's event handlers so a
  // status response fetched before a real event is discarded instead of clobbering the state
  // the event set.
  const invalidateSeriesRefresh = useOperationResync(OperationKeys.seriesRefresh, (status) => {
    if (status.isRunning) {
      setRefreshingSeries(true);
      setSeriesProgress((prev) =>
        prev && prev.total > 0
          ? prev
          : { processed: status.processed, total: status.total, succeeded: 0, failed: 0 },
      );
    } else {
      setRefreshingSeries(false);
      setSeriesProgress(null);
    }
  });

  useSignalREvent<RefreshProgressPayload>(SignalREvents.SeriesRefreshProgress, (data) => {
    invalidateSeriesRefresh();
    setRefreshingSeries(true);
    setSeriesProgress(data);
  });

  useSignalREvent<RefreshCompletePayload>(SignalREvents.SeriesRefreshComplete, (data) => {
    invalidateSeriesRefresh();
    setRefreshingSeries(false);
    setSeriesProgress(null);
    if (data.stopReason) {
      notifications.warning(
        `Series refresh stopped: ${data.stopReason}. ${data.totalSucceeded} refreshed, ${data.totalFailed} failed.`,
      );
    } else {
      notifications.success(
        `Series refresh complete: ${data.totalSucceeded} refreshed, ${data.totalFailed} failed`,
      );
    }
    void queryClient.invalidateQueries({ queryKey: queryKeys.seriesPending.all() });
    void queryClient.invalidateQueries({ queryKey: queryKeys.seriesConsistencyIssues.all() });
  });

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

      <Card className="space-y-3 p-4">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
          <div className="space-y-1.5">
            <label className="text-muted-foreground text-xs font-semibold uppercase">
              Refresh matched series from their online source
            </label>
            <p className="text-muted-foreground text-xs">
              Re-fetches every matched series' roster. Series whose source now differs from the
              library get a reviewable pending snapshot below; no-change series have nothing
              pending.
            </p>
          </div>

          <Button
            onClick={() => {
              void startBulkSeriesRefresh();
            }}
            disabled={refreshingSeries}
            className="w-full sm:w-auto"
          >
            {refreshingSeries ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <RefreshCw className="mr-2 h-4 w-4" />
            )}
            {refreshingSeries ? "Refreshing series..." : "Refresh Series"}
          </Button>
        </div>

        {refreshingSeries && seriesProgress && (
          <OperationProgressBar
            processed={seriesProgress.processed}
            total={seriesProgress.total}
            label="Refreshing series..."
            subText={`${seriesProgress.succeeded} refreshed, ${seriesProgress.failed} failed`}
          />
        )}
      </Card>

      <SeriesRefreshPendingList />

      <SeriesConsistencyIssueList />

      <AuthorConsistencyIssueList />

      <div className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-foreground text-lg font-bold">
            Books with Pending Metadata Changes ({totalCount})
          </h2>

          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={reevaluateMutation.isPending}
              onClick={() => reevaluateMutation.mutate()}
              title="Re-diff every pending change against the library, series mapping patterns, and changed-fields logic as they stand right now, without re-fetching anything from a source"
            >
              {reevaluateMutation.isPending ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <ListRestart className="mr-2 h-4 w-4" />
              )}
              Re-evaluate Pending Changes
            </Button>

            <DropdownMenu>
              <DropdownMenuTrigger
                render={
                  <Button variant="outline" size="sm">
                    <Filter className="mr-2 h-4 w-4" />
                    Filter fields{fieldFilter.size > 0 ? ` (${fieldFilter.size})` : ""}
                  </Button>
                }
              />
              <DropdownMenuContent>
                {METADATA_REFRESH_FIELDS.map((field) => (
                  <DropdownMenuCheckboxItem
                    key={field}
                    checked={fieldFilter.has(field)}
                    onCheckedChange={() => toggleFieldFilter(field)}
                  >
                    {METADATA_REFRESH_FIELD_LABELS[field] ?? field}
                  </DropdownMenuCheckboxItem>
                ))}
              </DropdownMenuContent>
            </DropdownMenu>

            <DropdownMenu>
              <DropdownMenuTrigger
                render={
                  <Button variant="outline" size="sm">
                    <Filter className="mr-2 h-4 w-4" />
                    Filter sources{sourceFilter.size > 0 ? ` (${sourceFilter.size})` : ""}
                  </Button>
                }
              />
              <DropdownMenuContent>
                {services.map((service) => (
                  <DropdownMenuCheckboxItem
                    key={service.name}
                    checked={sourceFilter.has(service.name)}
                    onCheckedChange={() => toggleSourceFilter(service.name)}
                  >
                    {service.name}
                  </DropdownMenuCheckboxItem>
                ))}
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>

        {hasActiveFilter && (
          <p className="text-muted-foreground text-xs">
            {fieldFilter.size > 0 &&
              "Showing only books whose pending changes are entirely within the selected fields."}
            {fieldFilter.size > 0 && sourceFilter.size > 0 && " "}
            {sourceFilter.size > 0 &&
              "Showing only books whose pending change came from the selected sources."}
          </p>
        )}

        {(selectedIds.size > 0 || (hasActiveFilter && totalCount > 0)) && (
          <Card className="flex flex-wrap items-center justify-between gap-3 p-3">
            <span className="text-sm">
              {selectedIds.size > 0 ? `${selectedIds.size} book(s) selected` : "No books selected"}
            </span>
            <div className="flex flex-wrap items-center gap-2">
              <Button
                size="sm"
                variant="outline"
                disabled={selectedIds.size === 0 || applying}
                onClick={() => applySelectedMutation.mutate()}
              >
                {applying ? <Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" /> : null}
                Apply Selected ({selectedIds.size})
              </Button>
              {fieldFilter.size > 0 && (
                <Button
                  size="sm"
                  disabled={applying || totalCount === 0}
                  onClick={() => applyFilteredMutation.mutate()}
                >
                  {applying ? <Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" /> : null}
                  Apply All Matching Filter ({totalCount})
                </Button>
              )}
              <Button
                size="sm"
                variant="destructive"
                disabled={selectedIds.size === 0 || dismissing}
                onClick={() => dismissSelectedMutation.mutate()}
              >
                {dismissing ? <Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" /> : null}
                Dismiss Selected ({selectedIds.size})
              </Button>
            </div>
          </Card>
        )}

        {applying && applyProgress && (
          <OperationProgressBar
            processed={applyProgress.processed}
            total={applyProgress.total}
            label="Applying metadata changes..."
            subText={`${applyProgress.succeeded} applied, ${applyProgress.failed} failed`}
          />
        )}

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
              {hasActiveFilter
                ? "No pending books match the selected filters."
                : "Refreshing a book with differing metadata stores a reviewable snapshot here. Books whose metadata already matches their source have nothing pending."}
            </p>
          </Card>
        ) : (
          <div className="space-y-2">
            <div className="flex items-center gap-2 px-1">
              <Checkbox checked={allOnPageSelected} onCheckedChange={toggleSelectAllOnPage} />
              <span className="text-muted-foreground text-xs">Select shown ({pageIds.length})</span>
            </div>

            {pendingItems.map((item) => {
              const isExpanded = expandedId === item.audiobookId;
              return (
                <div
                  key={item.audiobookId}
                  className="border-border bg-card overflow-hidden rounded-lg border"
                >
                  <div className="flex items-center gap-3 p-3">
                    <Checkbox
                      checked={selectedIds.has(item.audiobookId)}
                      onCheckedChange={() => toggleSelected(item.audiobookId)}
                    />
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
                          Pending since {formatDateTime(item.fetchedAt)}
                        </div>
                        <div className="mt-1.5 flex flex-wrap gap-1">
                          {item.changedFields.map((field) => (
                            <Badge key={field} variant="outline" className="text-[10px]">
                              {METADATA_REFRESH_FIELD_LABELS[field] ?? field}
                            </Badge>
                          ))}
                        </div>
                      </div>
                    </button>
                    <Badge variant="secondary" className="shrink-0">
                      {item.sourceName}
                    </Badge>
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
                  </div>

                  {isExpanded && (
                    <div className="border-border bg-muted/20 border-t p-3">
                      <PendingRefreshRowPanel
                        audiobookId={item.audiobookId}
                        onApplied={() => setExpandedId(null)}
                      />
                    </div>
                  )}
                </div>
              );
            })}

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
