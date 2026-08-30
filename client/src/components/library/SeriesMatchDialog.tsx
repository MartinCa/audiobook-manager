import { useState } from "react";
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
  series: SeriesOverview[];
  onMatched?: () => void;
}

export function SeriesMatchDialog({
  open,
  onOpenChange,
  series,
  onMatched,
}: SeriesMatchDialogProps) {
  const [threshold, setThreshold] = useState(0.85);
  const [selected, setSelected] = useState<string[]>([]);
  const [suggestions, setSuggestions] = useState<Record<string, SeriesMatchCandidate | null>>({});
  const [loadingSuggestions, setLoadingSuggestions] = useState(false);

  const [matching, setMatching] = useState(false);
  const [matchProgress, setMatchProgress] = useState<SeriesMatchProgressPayload | null>(null);

  const [prevOpen, setPrevOpen] = useState(open);
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) {
      setSelected(series.map((s) => s.name));
      setSuggestions({});
    }
  }

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

  const allSelected = series.length > 0 && selected.length === series.length;

  const toggleAll = () => {
    setSelected(allSelected ? [] : series.map((s) => s.name));
  };

  const toggleOne = (name: string) => {
    setSelected((prev) => (prev.includes(name) ? prev.filter((n) => n !== name) : [...prev, name]));
  };

  const handleLoadSuggestions = async () => {
    setLoadingSuggestions(true);
    try {
      for (const item of series) {
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
    if (selected.length === 0) return;
    setMatching(true);
    try {
      await seriesApi.startBulkMatch(threshold, selected);
      toast.success("Bulk series matching started in background");
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setMatching(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[85vh] max-w-2xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Bulk Match Series</DialogTitle>
        </DialogHeader>

        <div className="space-y-4 py-2 text-xs">
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

          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <Button
                variant="outline"
                size="sm"
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
              <Button variant="ghost" size="sm" disabled={matching} onClick={toggleAll}>
                {allSelected ? "Deselect All" : "Select All"}
              </Button>
            </div>
            <span className="text-muted-foreground">
              {selected.length} of {series.length} selected
            </span>
          </div>

          {matching && matchProgress && (
            <OperationProgressBar
              processed={matchProgress.processed}
              total={matchProgress.total}
              label={`Matching series (${matchProgress.succeeded} succeeded, ${matchProgress.failed} failed)`}
            />
          )}

          {series.length === 0 ? (
            <div className="text-muted-foreground py-6 text-center">
              Every series is already matched.
            </div>
          ) : (
            <div className="border-border max-h-72 divide-y overflow-y-auto rounded-md border">
              {series.map((item) => {
                const isSelected = selected.includes(item.name);
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
                      className="mt-0.5"
                    />
                    <div className="min-w-0 flex-1">
                      <div className="text-foreground font-medium">{item.name}</div>
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
                              <span className="flex items-center gap-1">
                                best: <strong>{best.seriesName}</strong> ({best.sourceName},{" "}
                                {Math.round(best.confidence * 100)}%)
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
            </div>
          )}

          <div className="border-border flex justify-end gap-2 border-t pt-4">
            <Button variant="outline" disabled={matching} onClick={() => onOpenChange(false)}>
              Close
            </Button>
            <Button
              disabled={matching || selected.length === 0}
              onClick={() => {
                void handleStartMatch();
              }}
            >
              {matching ? (
                <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
              ) : (
                <Search className="mr-1.5 h-4 w-4" />
              )}
              Match Selected ({selected.length})
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export default SeriesMatchDialog;
