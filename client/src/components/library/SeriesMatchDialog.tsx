import { useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Search, Loader2 } from "lucide-react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import { OperationProgressBar } from "@/components/OperationProgressBar";
import { seriesApi } from "@/services/api";
import { useSignalREvent } from "@/hooks/useSignalR";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
import type { SeriesMatchCandidate, SeriesOverview } from "@/types/Series";

// Cap on how many rows "Preview Suggestions" looks each source up for. The unmatched list is
// paged, but a page can still hold a page-size worth of rows, and every row is one sequential
// satellite request per configured source - so previewing is capped regardless of page size and
// the dialog says so.
const PREVIEW_SUGGESTION_CAP = 20;

const PAGE_SIZE = 50;

interface SeriesMatchProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface SeriesMatchCompletePayload {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
  stopReason?: string;
}

interface SeriesMatchDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onMatched?: () => void;
}

export function SeriesMatchDialog({ open, onOpenChange, onMatched }: SeriesMatchDialogProps) {
  const [threshold, setThreshold] = useState(0.85);
  // Selection is page-scoped like CleanBookUrls: null means "not customized yet" - defaults to
  // everything on the loaded page selected - and once the user toggles a box we switch to an
  // explicit set. The set is keyed by series name and persists across pages, so names selected
  // on page 1 stay selected on page 2.
  const [customSelection, setCustomSelection] = useState<Set<string> | null>(null);
  const [page, setPage] = useState(0);
  const [suggestions, setSuggestions] = useState<Record<string, SeriesMatchCandidate | null>>({});
  const [loadingSuggestions, setLoadingSuggestions] = useState(false);

  const [matching, setMatching] = useState(false);
  const [matchProgress, setMatchProgress] = useState<SeriesMatchProgressPayload | null>(null);

  const { data: pageData, isLoading: loadingPage } = useQuery({
    queryKey: ["series", "unmatched", page],
    queryFn: () => seriesApi.getSeriesPage(page, PAGE_SIZE, undefined, false),
    enabled: open,
    placeholderData: keepPreviousData,
  });

  const series = (pageData?.items ?? []) as SeriesOverview[];
  const totalCount = pageData?.totalCount ?? 0;
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount - 1);

  const [prevOpen, setPrevOpen] = useState(open);
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) {
      // Opening defaults to "everything on the first page selected", exactly like CleanBookUrls.
      setCustomSelection(null);
      setSuggestions({});
      setPage(0);
    }
  }

  const selectedIds = customSelection ?? new Set(series.map((s) => s.name));
  const selectedCount = customSelection ? customSelection.size : series.length;

  useSignalREvent<SeriesMatchProgressPayload>("SeriesMatchProgress", (payload) => {
    setMatching(true);
    setMatchProgress(payload);
  });

  useSignalREvent<SeriesMatchCompletePayload>("SeriesMatchComplete", (payload) => {
    setMatching(false);
    setMatchProgress(null);
    const msg = payload.stopReason
      ? `Matching stopped after ${payload.totalProcessed} series: ${payload.stopReason}`
      : `Matching complete: ${payload.totalSucceeded} of ${payload.totalProcessed} series matched${
          payload.totalFailed > 0 ? ` (${payload.totalFailed} failed)` : ""
        }`;
    toast.success(msg);
    onMatched?.();
  });

  const toggleOne = (name: string) => {
    const next = new Set(selectedIds);
    if (next.has(name)) {
      next.delete(name);
    } else {
      next.add(name);
    }
    setCustomSelection(next);
  };

  const selectAllOnPage = () => setCustomSelection(new Set(series.map((s) => s.name)));
  const clearSelection = () => setCustomSelection(new Set());

  // "Preview Suggestions" used to loop over every unmatched series - one sequential request per
  // row. With the list paged, only the current page is present; even a page can be large, so the
  // preview is capped explicitly and the cap is stated in the UI.
  const handleLoadSuggestions = async () => {
    const previewable = series.slice(0, PREVIEW_SUGGESTION_CAP);
    setLoadingSuggestions(true);
    try {
      for (const item of previewable) {
        try {
          const candidates = await seriesApi.getMatchCandidates(item.name);
          setSuggestions((prev) => ({
            ...prev,
            [item.name]: candidates[0] ?? null,
          }));
        } catch {
          setSuggestions((prev) => ({ ...prev, [item.name]: null }));
        }
      }
    } finally {
      setLoadingSuggestions(false);
    }
  };

  const handleStartMatch = async () => {
    if (selectedCount === 0) return;
    setMatching(true);
    try {
      await seriesApi.startBulkMatch(threshold, Array.from(selectedIds));
      toast.success("Bulk series matching started in background");
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setMatching(false);
    }
  };

  // The one-click path for "just match everything": the backend's BulkMatchSeriesDto treats a
  // missing SeriesNames list as "no subset given", so no client-side enumeration is needed.
  const handleMatchAll = async () => {
    if (totalCount === 0) return;
    setMatching(true);
    try {
      await seriesApi.startBulkMatch(threshold);
      toast.success("Bulk series matching started in background");
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setMatching(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[90dvh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-2xl sm:p-6">
        <DialogHeader>
          <DialogTitle>Bulk Match Series</DialogTitle>
        </DialogHeader>

        <div className="flex-1 space-y-4 overflow-y-auto py-2 text-xs">
          <p className="text-muted-foreground">
            Each unmatched series is looked up at every metadata source that supports series
            lookups. Only series whose best candidate scores at or above the threshold are matched;
            the rest are skipped.
          </p>

          <div className="border-border bg-muted/40 space-y-3 rounded-lg border p-4">
            <div className="flex items-center justify-between">
              <span className="font-semibold">Confidence Threshold</span>
              <span className="font-mono font-medium">{Math.round(threshold * 100)}%</span>
            </div>
            <input
              type="range"
              value={threshold}
              min={0.5}
              max={1.0}
              step={0.01}
              onChange={(e) => setThreshold(parseFloat(e.target.value))}
              disabled={matching}
              className="accent-primary bg-muted h-2 w-full cursor-pointer rounded-lg"
            />
          </div>

          <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-center">
            <div className="flex flex-wrap items-center gap-2">
              <Button
                variant="outline"
                size="sm"
                className="w-full sm:w-auto"
                disabled={loadingSuggestions || matching || series.length === 0}
                onClick={() => {
                  void handleLoadSuggestions();
                }}
              >
                {loadingSuggestions ? (
                  <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
                ) : (
                  <Search className="mr-1.5 h-3.5 w-3.5" />
                )}
                Preview Suggestions
              </Button>
              <Button
                variant="ghost"
                size="sm"
                className="w-full sm:w-auto"
                disabled={matching}
                onClick={selectedCount === series.length ? clearSelection : selectAllOnPage}
              >
                {selectedCount === series.length ? "Clear Selection" : "Select all on page"}
              </Button>
            </div>
            <span className="text-muted-foreground text-[11px] sm:text-xs">
              {selectedCount} of {totalCount} unmatched selected
            </span>
          </div>

          {matching && matchProgress && (
            <OperationProgressBar
              processed={matchProgress.processed}
              total={matchProgress.total}
              label={`Matching series (${matchProgress.succeeded} succeeded, ${matchProgress.failed} failed)`}
            />
          )}

          {loadingPage && series.length === 0 ? (
            <div className="text-muted-foreground flex items-center justify-center py-8">
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              <span>Loading unmatched series...</span>
            </div>
          ) : totalCount === 0 ? (
            <div className="text-muted-foreground py-6 text-center">
              Every series is already matched.
            </div>
          ) : (
            <div className="border-border divide-y rounded-md border">
              {series.map((item) => {
                const isSelected = selectedIds.has(item.name);
                const best = suggestions[item.name];
                return (
                  <div
                    key={item.name}
                    className="hover:bg-muted/50 flex items-start gap-3 p-2.5 transition-colors"
                  >
                    <Checkbox
                      checked={isSelected}
                      disabled={matching}
                      onCheckedChange={() => toggleOne(item.name)}
                      className="mt-0.5 shrink-0"
                    />
                    <div className="min-w-0 flex-1">
                      <div className="text-foreground font-medium break-words">{item.name}</div>
                      <div className="text-muted-foreground flex flex-wrap items-center gap-1.5 text-[11px]">
                        <span>{(item.authors ?? []).join(", ") || "Unknown author"}</span>
                        <span>&middot;</span>
                        <span>{item.ownedBookCount} owned</span>
                        {best !== undefined && (
                          <>
                            <span>&middot;</span>
                            {best === null ? (
                              <span className="italic">no candidates found</span>
                            ) : (
                              <span className="flex flex-wrap items-center gap-1">
                                best: <strong className="break-words">{best.seriesName}</strong> (
                                {best.sourceName}, {Math.round(best.confidence * 100)}%)
                                {best.confidence < threshold && (
                                  <Badge variant="secondary" className="px-1 text-[10px]">
                                    below threshold
                                  </Badge>
                                )}
                              </span>
                            )}
                          </>
                        )}
                      </div>
                    </div>
                  </div>
                );
              })}

              {pageCount > 1 && (
                <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-2.5">
                  <span className="text-muted-foreground text-[11px]">
                    Showing {currentPage * PAGE_SIZE + 1}–
                    {Math.min((currentPage + 1) * PAGE_SIZE, totalCount)} of {totalCount}
                  </span>
                  <div className="flex items-center gap-1.5">
                    <Button
                      size="sm"
                      variant="ghost"
                      className="h-6 px-1.5 text-[11px]"
                      disabled={currentPage === 0}
                      onClick={() => setPage(currentPage - 1)}
                    >
                      Previous
                    </Button>
                    <Button
                      size="sm"
                      variant="ghost"
                      className="h-6 px-1.5 text-[11px]"
                      disabled={currentPage >= pageCount - 1}
                      onClick={() => setPage(currentPage + 1)}
                    >
                      Next
                    </Button>
                  </div>
                </div>
              )}

              {series.length > PREVIEW_SUGGESTION_CAP && (
                <p className="text-muted-foreground px-2.5 py-2 italic">
                  Suggestions are previewed for the first {PREVIEW_SUGGESTION_CAP} series per page
                  to bound the lookups.
                </p>
              )}
            </div>
          )}
        </div>

        <div className="border-border flex flex-col-reverse items-stretch justify-end gap-2 border-t pt-3 sm:flex-row sm:items-center sm:pt-4">
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            disabled={matching}
            onClick={() => onOpenChange(false)}
          >
            Close
          </Button>
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            disabled={matching || totalCount === 0}
            onClick={() => {
              void handleMatchAll();
            }}
          >
            Match All Unmatched ({totalCount})
          </Button>
          <Button
            className="w-full sm:w-auto"
            disabled={matching || selectedCount === 0}
            onClick={() => {
              void handleStartMatch();
            }}
          >
            {matching ? (
              <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
            ) : (
              <Search className="mr-1.5 h-4 w-4" />
            )}
            Match Selected ({selectedCount})
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export default SeriesMatchDialog;
