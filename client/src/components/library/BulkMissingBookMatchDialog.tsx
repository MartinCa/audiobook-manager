import { useState } from "react";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertCircle, ExternalLink, Loader2, RefreshCcw, Wrench } from "lucide-react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { OperationProgressBar } from "@/components/OperationProgressBar";
import { PAGE_SIZE } from "@/constants/paging";
import { OperationKeys, SignalREvents } from "@/constants/signalrEvents";
import { seriesApi } from "@/services/api";
import { useSignalREvent } from "@/hooks/useSignalR";
import { useOperationResync } from "@/hooks/useOperationResync";
import { useClampedPage } from "@/hooks/useClampedPage";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type {
  ApplyMissingBookSelection,
  SeriesBookCandidate,
  SeriesBulkCandidateItem,
  SeriesExpectedBook,
} from "@/types/Series";

interface ApplyProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface ApplyCompletePayload {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
}

/** The per-row selection: the roster entry's natural key fields plus the chosen audiobook (0 = do not assign). */
interface RowSelection {
  position?: string | null;
  title?: string | null;
  audiobookId: number;
}

// The roster entry's natural key - the same identity the apply endpoint addresses by, and the
// stable key selections survive on across page flips (see the bounded-list invariant: a page is
// a slice of the missing list, so a selection made on one page must survive navigating away and
// back). The NUL separator keeps an odd position from merging with the title.
function bookKey(book: SeriesExpectedBook): string {
  return `${book.position ?? ""}\u0000${book.title}`;
}

// The roster entry's display name; shared by the row heading and the candidate Select triggers'
// programmatic labels so the trigger is identifiable to a screen-reader user on its own.
function bookLabel(book: SeriesExpectedBook): string {
  return `${book.position ? `Part ${book.position} — ` : ""}${book.title}`;
}

function defaultSelection(item: SeriesBulkCandidateItem, used: Set<number>): RowSelection {
  // The backend returns candidates fully ranked, so the best candidate is the first one not
  // already assigned to a different reviewed book: an automatic duplicate would dead-end the
  // apply, and a reviewer can still assign it explicitly once the rows are on screen. A row
  // whose every candidate is taken starts as "Do not assign".
  const candidate = item.candidates.find((c) => !used.has(c.audiobookId));
  return {
    position: item.book.position,
    title: item.book.title,
    audiobookId: candidate?.audiobookId ?? 0,
  };
}

function candidateLabel(c: SeriesBookCandidate): string {
  return `${c.bookName} — ${(c.authors ?? []).length ? (c.authors ?? []).join(", ") : "Unknown author"} (${Math.round(c.titleSimilarity * 100)}% title)`;
}

function selectionValue(
  selection: RowSelection | undefined,
  item: SeriesBulkCandidateItem,
): string {
  // Until the render-time preselect below has recorded the row, display the server-ranked best
  // candidate directly so a row never briefly shows "Do not assign" before its data settles.
  const audiobookId = selection ? selection.audiobookId : (item.candidates[0]?.audiobookId ?? 0);
  return audiobookId > 0 ? String(audiobookId) : "";
}

interface BulkMissingBookMatchDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  seriesName: string;
}

export function BulkMissingBookMatchDialog({
  open,
  onOpenChange,
  seriesName,
}: BulkMissingBookMatchDialogProps) {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(0);
  const [selections, setSelections] = useState<Record<string, RowSelection>>({});
  const [applying, setApplying] = useState(false);
  const [applyProgress, setApplyProgress] = useState<ApplyProgressPayload | null>(null);
  const [resumed, setResumed] = useState(false);

  // Bumped on every closed->open transition and fed to useOperationResync below: the component
  // stays mounted (only the portal unmounts on close), so without this the only status fetch the
  // hook would ever issue is the one at page load - an apply started elsewhere while the dialog
  // was closed would be invisible when it opens. The bump re-runs the hook's fetch effect, and
  // that effect instance's cleanup drops any in-flight response from the previous window.
  const [resyncGeneration, setResyncGeneration] = useState(0);

  // The component stays mounted (only the portal unmounts on close), so selection state must
  // not outlive the dialog's identity: closing/reopening, or navigating to a different series,
  // must start from a fresh review, never a stale one from a previous run. Reset during render
  // whenever the identity changes - the lint-sanctioned "adjust state when something changes"
  // pattern (guarded setState during render, no effect), and the guard keeps it from re-triggering.
  const dialogIdentity = `${open}:${seriesName}`;
  const [lastDialogIdentity, setLastDialogIdentity] = useState(dialogIdentity);
  if (lastDialogIdentity !== dialogIdentity) {
    setLastDialogIdentity(dialogIdentity);
    setSelections({});
    setPage(0);
    setApplying(false);
    setApplyProgress(null);
    setResumed(false);
  }

  // Same guarded render-time shape, but specifically for the open transition (the identity reset
  // above fires on close and series changes too): mounted-open renders bump nothing, so they stay
  // on the single mount-time status fetch with no race between two concurrent fetches.
  const [prevOpen, setPrevOpen] = useState(open);
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) {
      setResyncGeneration((g) => g + 1);
    }
  }

  const {
    data: pageData,
    isFetching,
    isError,
    refetch,
  } = useQuery({
    queryKey: ["seriesBulkMissingCandidates", seriesName, page],
    queryFn: () => seriesApi.getBulkMissingBookCandidates(seriesName, page, PAGE_SIZE),
    enabled: open,
    // Keep the previous page rendered while the next one loads; the query key holds the page.
    placeholderData: keepPreviousData,
  });

  const items = pageData?.items ?? [];
  const totalCount = pageData?.totalCount ?? 0;
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount - 1);

  // The page cursor is clamped here and by useClampedPage (SeriesOverview's shape): the total that
  // sizes the pager comes from this very response, so an out-of-range fetch is unavoidable for the
  // response that reveals a shrink - but the clamp keeps every later fetch on a valid page. Display
  // and pager draw only what currentPage says; the hook pulls the raw page state back into range
  // once the shrink is visible, so the next fetch for a user (or invalidation) never misses again.
  useClampedPage(page, pageCount, setPage);

  // Preselect the best (server-ranked first) candidate for each missing book the first time its
  // row is seen, without ever overwriting an explicit choice - including "Do not assign", which
  // is stored as audiobookId 0. A candidate another reviewed book already took is skipped, so the
  // preselection can never hand the apply a duplicate assignment. Same guarded render-time state
  // shape the identity reset above uses: a per-row default is external data becoming local state,
  // and once every rendered row has an entry the guard stops firing, so it never cascades.
  let nextSelections: Record<string, RowSelection> | null = null;
  for (const item of items) {
    const key = bookKey(item.book);
    if (selections[key] === undefined) {
      const base = nextSelections ?? selections;
      const used = new Set<number>();
      for (const row of Object.values(base)) {
        if (row.audiobookId > 0) used.add(row.audiobookId);
      }
      (nextSelections ??= { ...selections })[key] = defaultSelection(item, used);
    }
  }
  if (nextSelections !== null) {
    setSelections(nextSelections);
  }
  // Render the entries just preselected in this pass (setState during render commits before the
  // browser handles the next event), so a row never flickers between its final value and the
  // pre-skip "best candidate" fallback.
  const renderedSelections = nextSelections ?? selections;

  useSignalREvent<ApplyProgressPayload>(SignalREvents.SeriesMissingBookApplyProgress, (data) => {
    setResumed(false);
    setApplying(true);
    setApplyProgress(data);
  });

  useSignalREvent<ApplyCompletePayload>(SignalREvents.SeriesMissingBookApplyComplete, (data) => {
    setResumed(false);
    setApplying(false);
    setApplyProgress(null);
    notifications.success(
      `Bulk match complete: ${data.totalSucceeded} applied${data.totalFailed > 0 ? `, ${data.totalFailed} failed` : ""}`,
    );
    // The review is now stale (the applied books are no longer missing); the series detail is
    // invalidated and the dialog closes to show the fresh state.
    setSelections({});
    onOpenChange(false);
    void queryClient.invalidateQueries({ queryKey: ["seriesDetail", seriesName] });
    void queryClient.invalidateQueries({ queryKey: ["series"] });
  });

  // Recover on mount, after a SignalR reconnect, and every time the dialog opens: an apply that
  // started elsewhere - or whose events were missed while disconnected - must not leave this
  // dialog looking idle. The component never remounts when the dialog opens (only the portal
  // unmounts), so resyncGeneration (bumped on each open transition) is what makes the hook
  // re-fetch instead of trusting the one mount-time fetch from page load; the identity reset
  // above wiped any applying state that events delivered while the dialog was closed, so without
  // it a freshly-opened dialog would show idle even for a batch that is genuinely running. The
  // status endpoint only carries processed/total, so the progress bar switches to a "resuming"
  // label rather than fabricating the "(0 succeeded, 0 failed)" of a fresh batch; the next live
  // progress event replaces it with the real counts.
  useOperationResync(
    OperationKeys.seriesMissingBookApply,
    (status) => {
      if (status.isRunning) {
        setResumed(true);
        setApplying(true);
        setApplyProgress({
          processed: status.processed,
          total: status.total,
          succeeded: 0,
          failed: 0,
        });
      } else {
        setResumed(false);
        setApplying(false);
        setApplyProgress(null);
      }
    },
    resyncGeneration,
  );

  const selectedSelections = Object.values(renderedSelections).filter((s) => s.audiobookId > 0);

  // One library book can only be assigned to one missing slot (the server rejects a batch that
  // tries; refuse it here first with a message pointing at the row to free). Preselection stays
  // duplicate-free by construction, so this only fires on an explicit choice.
  const handleSelectChange = (key: string, item: SeriesBulkCandidateItem, value: string | null) => {
    const audiobookId = value ? Number(value) : 0;
    if (audiobookId > 0) {
      const owner = Object.entries(selections).find(
        ([otherKey, other]) => otherKey !== key && other.audiobookId === audiobookId,
      );
      if (owner) {
        const other = owner[1];
        const otherName = `${other.position ? `Part ${other.position} — ` : ""}${other.title ?? "(untitled missing book)"}`;
        const chosenName =
          item.candidates.find((c) => c.audiobookId === audiobookId)?.bookName ??
          `audiobook ${audiobookId}`;
        notifications.error(
          `"${chosenName}" is already assigned to "${otherName}". Set that book to "Do not assign" first, then pick it here.`,
        );
        return;
      }
    }
    setSelections((prev) => ({
      ...prev,
      [key]: {
        position: item.book.position,
        title: item.book.title,
        audiobookId,
      },
    }));
  };

  const handleApply = async () => {
    if (applying || selectedSelections.length === 0) return;
    const payload = selectedSelections.map<ApplyMissingBookSelection>((s) => ({
      position: s.position || undefined,
      title: s.title || undefined,
      audiobookId: s.audiobookId,
    }));
    setApplying(true);
    setApplyProgress(null);
    try {
      await seriesApi.startBulkMissingBookApply(seriesName, payload);
      notifications.success(
        `Bulk match queued for ${payload.length} book${payload.length !== 1 ? "s" : ""}`,
      );
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
      setApplying(false);
    }
  };

  const handleClose = () => {
    if (applying) return;
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="flex max-h-[85vh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-4xl sm:p-6">
        <DialogHeader>
          <DialogTitle>Match Missing Books</DialogTitle>
        </DialogHeader>

        <div className="border-border bg-muted/30 mb-3 rounded-md border p-2.5 text-xs">
          For each missing book you review, the best library candidate is preselected; switch any
          book to an alternate candidate or "Do not assign" as you go. Selections carry across pages
          — applying sends every book you've reviewed at once, and books you never review stay
          unassigned.
        </div>

        {applying && applyProgress && (
          <OperationProgressBar
            processed={applyProgress.processed}
            total={applyProgress.total}
            label={
              resumed
                ? "Resuming apply in progress"
                : `Applying (${applyProgress.succeeded} succeeded, ${applyProgress.failed} failed)`
            }
          />
        )}

        <div className="flex-1 overflow-y-auto py-2">
          {isFetching && items.length === 0 ? (
            <div className="space-y-2">
              <div className="bg-muted h-11 w-full animate-pulse rounded" />
              <div className="bg-muted h-11 w-full animate-pulse rounded" />
              <div className="bg-muted h-11 w-full animate-pulse rounded" />
            </div>
          ) : isError ? (
            <div className="flex flex-col items-center justify-center py-8 text-center">
              <AlertCircle className="text-destructive mb-2 h-6 w-6" />
              <p className="text-muted-foreground text-sm">Failed to load missing books.</p>
              <Button variant="outline" size="sm" className="mt-3" onClick={() => void refetch()}>
                <RefreshCcw className="mr-1.5 h-3.5 w-3.5" />
                Retry
              </Button>
            </div>
          ) : items.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-8 text-center">
              <AlertCircle className="text-muted-foreground mb-2 h-6 w-6" />
              <p className="text-muted-foreground text-sm">
                No missing books to match in this series.
              </p>
            </div>
          ) : (
            <div className="space-y-3">
              {items.map((item) => {
                const key = bookKey(item.book);
                // The flat items list is what SelectValue renders into the (closed) trigger; the
                // matching SelectItems below are the open popup's list. Both must carry the same
                // values, or the trigger and the list disagree.
                const options = [
                  { value: "", label: "Do not assign" },
                  ...item.candidates.map((c: SeriesBookCandidate) => ({
                    value: String(c.audiobookId),
                    label: candidateLabel(c),
                  })),
                ];
                return (
                  <div
                    key={key}
                    className="border-border bg-card flex flex-col justify-between gap-2 rounded-md border p-2.5 sm:flex-row sm:items-center"
                  >
                    <div className="min-w-0 flex-1">
                      <span className="text-foreground font-semibold break-words">
                        {bookLabel(item.book)}
                      </span>
                      {item.book.year && (
                        <span className="text-muted-foreground"> ({item.book.year})</span>
                      )}
                      {item.book.sourceUrl && (
                        <a
                          href={item.book.sourceUrl}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="text-primary ml-1.5 flex items-center hover:underline"
                        >
                          <ExternalLink className="mr-0.5 h-3 w-3" />
                          Source
                        </a>
                      )}
                    </div>
                    <Select
                      value={selectionValue(renderedSelections[key], item)}
                      onValueChange={(v) => handleSelectChange(key, item, v)}
                      items={options}
                      disabled={applying}
                    >
                      <SelectTrigger
                        aria-label={`Library candidate for ${bookLabel(item.book)}`}
                        className="h-8 text-xs"
                      >
                        <SelectValue placeholder="Do not assign" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="">Do not assign</SelectItem>
                        {item.candidates.map((c: SeriesBookCandidate) => (
                          <SelectItem key={c.audiobookId} value={String(c.audiobookId)}>
                            {candidateLabel(c)}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                );
              })}

              {pageCount > 1 && (
                <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
                  <span className="text-muted-foreground text-xs">
                    Showing {currentPage * PAGE_SIZE + 1}–
                    {Math.min((currentPage + 1) * PAGE_SIZE, totalCount)} of {totalCount} missing
                    books
                  </span>
                  <div className="flex items-center gap-2">
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={currentPage === 0 || applying}
                      onClick={() => setPage(currentPage - 1)}
                    >
                      Previous
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={currentPage >= pageCount - 1 || applying}
                      onClick={() => setPage(currentPage + 1)}
                    >
                      Next
                    </Button>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>

        <div className="border-border flex flex-col-reverse justify-end gap-2 border-t pt-4 sm:flex-row sm:items-center">
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            disabled={applying}
            onClick={handleClose}
          >
            Cancel
          </Button>

          <p className="text-muted-foreground text-center text-xs sm:mr-auto sm:text-left">
            Assigns {selectedSelections.length} of {totalCount} missing books — books set to "Do not
            assign" or left unreviewed stay missing.
          </p>

          <Button
            className="w-full sm:w-auto"
            disabled={applying || selectedSelections.length === 0}
            onClick={() => {
              void handleApply();
            }}
          >
            {applying ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <Wrench className="mr-2 h-4 w-4" />
            )}
            {applying
              ? "Applying..."
              : `Apply ${selectedSelections.length} Assignment${selectedSelections.length !== 1 ? "s" : ""}`}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export default BulkMissingBookMatchDialog;
