import { useMemo, useState, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Check, Loader2, RefreshCcw, Trash2, ExternalLink } from "lucide-react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import { OperationProgressBar } from "@/components/OperationProgressBar";
import { LastRefreshedHint } from "@/components/LastRefreshedHint";
import { seriesApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useSignalREvent } from "@/hooks/useSignalR";
import { useOperationResync } from "@/hooks/useOperationResync";
import { OperationKeys, SignalREvents } from "@/constants/signalrEvents";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type { SeriesRefreshApplyRequest, SeriesRefreshChange } from "@/types/SeriesRefresh";

interface SeriesRefreshApplyProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface SeriesRefreshApplyCompletePayload {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
  /** The series the apply was requested for (the name this dialog's seriesName prop held when it
   *  started the apply). Completions are broadcast to every connection, so the dialog must match
   *  this before acting: a completion for a different series (a parallel tab, or an apply started
   *  from the metadata-refresh page) must not close or navigate this dialog. */
  seriesName: string;
  /** The series' name after the apply: the adopted source name when the rename fully succeeded,
   *  absent otherwise (no adoption, a no-op name, or a partial failure left the old name
   *  addressable). The caller navigates its route there when present. */
  effectiveSeriesName?: string | null;
}

function changeKey(c: SeriesRefreshChange): string {
  return `${c.changeType}:${c.audiobookId ?? ""}`;
}

interface SeriesRefreshPendingDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  seriesName: string;
  /**
   * Called after a successful apply (or dismiss) so the caller refreshes whatever it renders
   * from the pending list / series detail. `renamedTo` is the series' new name when the apply
   * fully adopted the source's series name (the caller must navigate its route there); null
   * when the series is still addressable under its original name.
   */
  onApplied?: (renamedTo?: string | null) => void;
}

/**
 * The shared pending series-refresh review: the explicit changes a refresh computed (part
 * updates, missing source books, part removals), each accepted or skipped, the optional
 * source-series-name adoption, and the Apply / Dismiss actions. Used from the series detail's
 * "review" banner and from the /library/metadata-refresh page's series pending list so both
 * surfaces apply through the same dialog and the same background operation.
 */
export function SeriesRefreshPendingDialog({
  open,
  onOpenChange,
  seriesName,
  onApplied,
}: SeriesRefreshPendingDialogProps) {
  const queryClient = useQueryClient();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [adoptName, setAdoptName] = useState(false);
  const [applying, setApplying] = useState(false);
  const [progress, setProgress] = useState<SeriesRefreshApplyProgressPayload | null>(null);

  const {
    data: pending,
    isFetching,
    isError,
    refetch,
  } = useQuery({
    queryKey: queryKeys.seriesPending.bySeries(seriesName),
    // The endpoint 404s when no snapshot exists; the API layer normalizes that to undefined and
    // the query layer maps it to null (TanStack Query's no-void-query-fn rule: queryFn must not
    // resolve undefined) - both represent the same "nothing pending" state.
    queryFn: () => seriesApi.getSeriesPending(seriesName).then((pending) => pending ?? null),
    enabled: open && Boolean(seriesName),
  });

  // When a fresh pending snapshot arrives for the open dialog, seed the defaults once: part
  // updates and removals are selected (applying is why the user opened the review), missing
  // books stay unselected until a library book is chosen for them. Close/reopen of the same
  // series re-seeds; a refetch superseding the snapshot (new fetch while open) re-seeds too.
  // This is the lint-sanctioned "adjust state during render when the identity changes" pattern
  // (guarded, so the adjustment never re-triggers itself), the same shape SeriesDetail uses to
  // reset its book selection when the series parameter changes.
  const seededKey = `${open}:${seriesName}:${pending?.fetchedAt ?? ""}`;
  const [lastSeededKey, setLastSeededKey] = useState<string | null>(null);
  if (lastSeededKey !== seededKey) {
    setLastSeededKey(seededKey);
    if (pending) {
      // A legacy PendingSeriesRefresh snapshot can still contain MissingBook entries (the enum
      // member is kept only so those old rows still deserialize - see UPCOMING_RELEASES_DESIGN.md).
      // They render nowhere any more and carry no audiobookId, so they can never actually be
      // applied; seeding them into the selection would inflate "Apply N" with a count that has
      // no visible row behind it.
      setSelected(
        new Set(pending.changes.filter((c) => c.changeType !== "MissingBook").map(changeKey)),
      );
      setAdoptName(false);
    }
  }

  const sourceName = pending?.sourceSeriesName?.trim();
  const hasAdoptableName = Boolean(sourceName) && sourceName !== seriesName;

  const changes = useMemo(() => pending?.changes ?? [], [pending]);
  const partUpdates = useMemo(
    () => changes.filter((c) => c.changeType === "PartUpdate"),
    [changes],
  );
  const partRemovals = useMemo(
    () => changes.filter((c) => c.changeType === "PartRemoval"),
    [changes],
  );

  const selectedCount = selected.size;
  const willAdoptSourceName = hasAdoptableName && adoptName;
  const readyToApply = selectedCount > 0 || willAdoptSourceName;

  function isSelected(key: string): boolean {
    return selected.has(key);
  }

  function toggle(key: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  const invalidateViews = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.seriesPending.all() });
    void queryClient.invalidateQueries({ queryKey: queryKeys.seriesDetail.bySeries(seriesName) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.series.all() });
    void queryClient.invalidateQueries({ queryKey: queryKeys.seriesCounts() });
  };

  // Recover an in-flight apply (e.g. after a SignalR reconnect) into this dialog's progress bar.
  // The dialog stays mounted while its portal is closed, so the mount-time status fetch happens
  // once at page load - passing `open` as the resync trigger re-fetches whenever the review is
  // actually shown, so an apply started while it was hidden is picked up instead of missed. The
  // returned invalidate is called from the apply's event handlers so a status response fetched
  // before a real event is discarded instead of clobbering the state the event set.
  const invalidateRefreshApply = useOperationResync(
    OperationKeys.seriesRefreshApply,
    (status) => {
      if (!applying) return;
      if (status.isRunning) {
        setProgress((prev) =>
          prev && prev.total > 0
            ? prev
            : { processed: status.processed, total: status.total, succeeded: 0, failed: 0 },
        );
      } else {
        setApplying(false);
        setProgress(null);
      }
    },
    open,
  );

  useSignalREvent<SeriesRefreshApplyProgressPayload>(
    SignalREvents.SeriesRefreshApplyProgress,
    (data) => {
      if (!applying) return;
      invalidateRefreshApply();
      setProgress(data);
    },
  );

  useSignalREvent<SeriesRefreshApplyCompletePayload>(
    SignalREvents.SeriesRefreshApplyComplete,
    (data) => {
      if (!applying) return;
      // The completion is broadcast connection-wide, not per-series: it belongs to the series
      // this dialog applied for. A completion for another series must not close this dialog,
      // toast its results or navigate on its rename - that series' own dialog (or a parallel
      // tab's) is the one listening for it.
      if (data.seriesName !== seriesName) return;
      invalidateRefreshApply();
      setApplying(false);
      setProgress(null);
      if (data.totalProcessed === 0) {
        // Nothing was applied: the pending row vanished between review and apply (dismissed
        // elsewhere, or superseded by a no-change refresh). Don't claim a success that didn't
        // happen - say so, and let the views reload to the real state.
        notifications.info("The pending changes were already gone - nothing was applied");
      } else if (data.totalFailed > 0) {
        const msg = `Applied ${data.totalSucceeded} of ${data.totalProcessed} changes (${data.totalFailed} failed)`;
        notifications.success(msg);
      } else {
        notifications.success(`Applied ${data.totalSucceeded} pending changes`);
      }
      invalidateViews();
      onApplied?.(data.effectiveSeriesName ?? null);
      onOpenChange(false);
    },
  );

  const handleClose = () => {
    if (applying) return;
    onOpenChange(false);
    onApplied?.(null);
  };

  const handleDismiss = async () => {
    if (applying) return;
    try {
      await seriesApi.dismissSeriesPending(seriesName);
      notifications.success("Pending changes discarded");
      invalidateViews();
      onApplied?.(null);
      onOpenChange(false);
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    }
  };

  const handleApply = async () => {
    if (applying || !readyToApply || !pending) return;

    const selections: SeriesRefreshApplyRequest["selections"] = [];
    for (const c of changes) {
      const key = changeKey(c);
      if (!selected.has(key)) continue;
      if (c.audiobookId == null) continue;
      selections.push({ changeType: c.changeType, audiobookId: c.audiobookId });
    }

    const request: SeriesRefreshApplyRequest = {
      adoptSourceSeriesName: willAdoptSourceName,
      selections,
    };

    if (request.selections.length === 0 && !request.adoptSourceSeriesName) return;

    setApplying(true);
    setProgress({
      processed: 0,
      total: request.selections.length + (request.adoptSourceSeriesName ? 1 : 0),
      succeeded: 0,
      failed: 0,
    });
    try {
      await seriesApi.applySeriesPending(seriesName, request);
    } catch (err: unknown) {
      setApplying(false);
      setProgress(null);
      notifications.error(handleApiError(err).message);
    }
  };

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="flex max-h-[88dvh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-3xl sm:p-6">
        <DialogHeader>
          <DialogTitle>Review Series Refresh — {pending?.sourceName ?? "source"}</DialogTitle>
        </DialogHeader>

        <div className="flex-1 overflow-y-auto py-2">
          {isFetching ? (
            <div className="text-muted-foreground flex flex-col items-center justify-center py-12">
              <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
              <p className="text-sm">Loading pending changes...</p>
            </div>
          ) : isError ? (
            <div className="flex flex-col items-center justify-center py-12 text-center">
              <AlertTriangle className="text-destructive mb-2 h-6 w-6" />
              <p className="text-muted-foreground text-sm">Failed to load pending changes.</p>
              <Button variant="outline" size="sm" className="mt-3" onClick={() => void refetch()}>
                <RefreshCcw className="mr-1.5 h-3.5 w-3.5" />
                Retry
              </Button>
            </div>
          ) : !pending ? (
            <div className="text-muted-foreground py-12 text-center text-sm">
              No pending changes for this series.
            </div>
          ) : (
            <div className="space-y-4">
              <div className="border-border bg-muted/40 flex flex-wrap items-center gap-x-4 gap-y-1 rounded-md border px-3 py-2 text-xs">
                {pending.sourceUrl && (
                  <a
                    href={pending.sourceUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-primary flex items-center hover:underline"
                  >
                    <ExternalLink className="mr-1 h-3 w-3" />
                    View series at source
                  </a>
                )}
                <span className="text-muted-foreground">
                  <LastRefreshedHint lastRefreshedAt={pending.fetchedAt} />
                </span>
                <Badge variant="secondary">{pending.changes.length} changes</Badge>
              </div>

              <p className="text-muted-foreground text-xs">
                Review the changes this refresh found. Applied changes update each book's tags and
                files; unchanged rows are skipped. Missing source books need a library book chosen
                before they can be applied.
              </p>

              {/* Part updates */}
              {partUpdates.length > 0 && (
                <ChangeSection title={`Part Updates (${partUpdates.length})`}>
                  {partUpdates.map((c) => {
                    const key = changeKey(c);
                    return (
                      <ChangeRow
                        key={key}
                        selected={isSelected(key)}
                        onToggle={() => toggle(key)}
                        title={`${c.bookName ?? "Unknown book"} · part ${c.storedPart ?? "—"} → ${c.newPart ?? "—"}`}
                        subtitle={
                          c.rosterTitle
                            ? `Source assigns part ${c.newPart ?? "—"} to "${c.rosterTitle}"`
                            : undefined
                        }
                      />
                    );
                  })}
                </ChangeSection>
              )}

              {/* Part removals */}
              {partRemovals.length > 0 && (
                <ChangeSection title={`Part Removals (${partRemovals.length})`}>
                  {partRemovals.map((c) => {
                    const key = changeKey(c);
                    return (
                      <ChangeRow
                        key={key}
                        selected={isSelected(key)}
                        onToggle={() => toggle(key)}
                        title={`${c.bookName ?? "Unknown book"} · part ${c.storedPart ?? "—"} → no part`}
                        subtitle="The source no longer lists this book, so its part has no basis."
                      />
                    );
                  })}
                </ChangeSection>
              )}

              {/* Series-name adoption */}
              {hasAdoptableName && (
                <div className="border-border bg-card rounded-md border p-3 text-xs">
                  <label className="flex cursor-pointer items-start gap-2">
                    <Checkbox
                      checked={adoptName}
                      onCheckedChange={(checked) => setAdoptName(Boolean(checked))}
                      aria-label="Adopt source series name"
                    />
                    <span>
                      <span className="text-foreground font-semibold">
                        Rename this series to match the source
                      </span>
                      <span className="text-muted-foreground block">
                        "{seriesName}" → "{pending.sourceSeriesName}". Updates every member book
                        through the normal save pipeline (tags, folder, sidecars).
                      </span>
                    </span>
                  </label>
                </div>
              )}

              {applying && progress && (
                <OperationProgressBar
                  compact
                  processed={progress.processed}
                  total={progress.total}
                  label="Applying pending changes..."
                  subText={`${progress.succeeded} applied, ${progress.failed} failed`}
                />
              )}
            </div>
          )}
        </div>

        <div className="border-border flex flex-col-reverse justify-end gap-2 border-t pt-4 sm:flex-row">
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            disabled={applying}
            onClick={handleClose}
          >
            Close
          </Button>
          <Button
            variant="outline"
            className="text-destructive hover:bg-destructive/10 border-destructive/30 hover:border-destructive/60 w-full sm:w-auto"
            disabled={applying || isFetching || (!pending && !isError)}
            onClick={() => {
              void handleDismiss();
            }}
          >
            <Trash2 className="mr-1.5 h-4 w-4" />
            Dismiss
          </Button>
          <Button
            className="w-full sm:w-auto"
            disabled={applying || !readyToApply || isFetching || !pending}
            onClick={() => {
              void handleApply();
            }}
          >
            {applying ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <Check className="mr-2 h-4 w-4" />
            )}
            {applying
              ? "Applying..."
              : selectedCount > 0
                ? willAdoptSourceName
                  ? `Apply ${selectedCount} + rename`
                  : `Apply ${selectedCount}`
                : willAdoptSourceName
                  ? "Apply rename"
                  : "Apply"}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function ChangeSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="space-y-1.5">
      <h3 className="text-foreground text-xs font-semibold uppercase">{title}</h3>
      <div className="space-y-1.5">{children}</div>
    </div>
  );
}

function ChangeRow({
  selected,
  onToggle,
  title,
  subtitle,
}: {
  selected: boolean;
  onToggle: () => void;
  title: string;
  subtitle?: string;
}) {
  return (
    <div className="border-border bg-card flex items-start gap-2 rounded-md border p-2.5 text-xs">
      <Checkbox
        checked={selected}
        onCheckedChange={onToggle}
        aria-label={title}
        className="mt-0.5"
      />
      <button type="button" onClick={onToggle} className="min-w-0 flex-1 cursor-pointer text-left">
        <span className="text-foreground block font-medium break-words">{title}</span>
        {subtitle && <span className="text-muted-foreground block break-words">{subtitle}</span>}
      </button>
    </div>
  );
}

export default SeriesRefreshPendingDialog;
