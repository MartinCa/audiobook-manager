import { useState, useMemo } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, AlertCircle, Check, BookOpen, RefreshCcw } from "lucide-react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { seriesApi } from "@/services/api";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
import type { SeriesBookCandidate } from "@/types/Series";

function getMatchLabel(candidate: SeriesBookCandidate): string {
  return candidate.authorMatches ? "Author + title match" : "Title match";
}

function getMatchClassName(candidate: SeriesBookCandidate): string {
  return candidate.authorMatches
    ? "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400"
    : "bg-blue-500/15 text-blue-600 dark:text-blue-400";
}

interface MissingBookCandidatesDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  seriesName: string;
  missingBook: { id: number; position?: string | null; title?: string | null };
}

export function MissingBookCandidatesDialog({
  open,
  onOpenChange,
  seriesName,
  missingBook,
}: MissingBookCandidatesDialogProps) {
  const queryClient = useQueryClient();
  const [selectedCandidate, setSelectedCandidate] = useState<SeriesBookCandidate | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [applying, setApplying] = useState(false);

  // The dialog component stays mounted (only the portal unmounts on close), so selection state
  // must not outlive the dialog's current identity: closing and reopening, or switching to a
  // different missing book, must always show the candidate list, never a stale confirmation
  // panel for a previous book. Reset during render whenever the identity changes - this is the
  // lint-sanctioned "adjust state when something changes" pattern (guarded setState during
  // render, no effect), and the guard keeps it from ever re-triggering itself.
  const dialogIdentity = `${open}:${missingBook.id}:${missingBook.position ?? ""}:${missingBook.title ?? ""}`;
  const [lastDialogIdentity, setLastDialogIdentity] = useState(dialogIdentity);
  if (lastDialogIdentity !== dialogIdentity) {
    setLastDialogIdentity(dialogIdentity);
    setConfirming(false);
    setSelectedCandidate(null);
  }

  const {
    data: candidates,
    isFetching,
    isError,
    refetch,
  } = useQuery({
    queryKey: ["missingBookCandidates", seriesName, missingBook.position, missingBook.title],
    queryFn: () =>
      seriesApi.getMissingBookCandidates(
        seriesName,
        missingBook.position ?? null,
        missingBook.title ?? null,
      ),
    enabled: open,
    staleTime: 30_000,
  });

  const rankedCandidates = useMemo(() => {
    if (!candidates) return [];
    return [...candidates].sort((a, b) => {
      if (a.authorMatches !== b.authorMatches) return a.authorMatches ? -1 : 1;
      return (b.titleSimilarity ?? 0) - (a.titleSimilarity ?? 0);
    });
  }, [candidates]);

  const handleSelect = (candidate: SeriesBookCandidate) => {
    setSelectedCandidate(candidate);
    setConfirming(true);
  };

  const handleConfirm = async () => {
    if (!selectedCandidate) return;
    // Snapshot the selection and the target slot: a close/identity reset cannot occur through
    // the modal UI mid-apply, but if one ever did, the success path must not deref a cleared
    // selection or apply to a slot that changed under it.
    const candidate = selectedCandidate;
    const targetPosition = missingBook.position ?? null;
    const targetTitle = missingBook.title ?? null;
    setApplying(true);
    try {
      await seriesApi.applyMissingBook(
        seriesName,
        candidate.audiobookId,
        targetPosition,
        targetTitle,
      );
      toast.success(`Applied "${candidate.bookName}" to missing book`);
      setConfirming(false);
      setSelectedCandidate(null);
      void queryClient.invalidateQueries({
        queryKey: ["seriesDetail"],
      });
      onOpenChange(false);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message, { duration: Infinity });
    } finally {
      setApplying(false);
    }
  };

  const handleClose = () => {
    if (applying) return;
    setConfirming(false);
    setSelectedCandidate(null);
    onOpenChange(false);
  };

  const handleBackToList = () => {
    setConfirming(false);
    setSelectedCandidate(null);
  };

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="flex max-h-[90dvh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-2xl sm:p-6">
        <DialogHeader>
          <DialogTitle>Candidates for Missing Book</DialogTitle>
        </DialogHeader>

        <div className="flex-1 overflow-y-auto py-2">
          {isFetching || !rankedCandidates ? (
            <div className="space-y-2">
              <div className="bg-muted h-9 w-full animate-pulse rounded" />
              <div className="bg-muted h-9 w-full animate-pulse rounded" />
              <div className="bg-muted h-9 w-full animate-pulse rounded" />
            </div>
          ) : isError ? (
            <div className="flex flex-col items-center justify-center py-8 text-center">
              <AlertCircle className="text-destructive mb-2 h-6 w-6" />
              <p className="text-muted-foreground text-sm">Failed to load candidates.</p>
              <Button variant="outline" size="sm" className="mt-3" onClick={() => void refetch()}>
                <RefreshCcw className="mr-1.5 h-3.5 w-3.5" />
                Retry
              </Button>
            </div>
          ) : rankedCandidates.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-8 text-center">
              <AlertCircle className="text-muted-foreground mb-2 h-6 w-6" />
              <p className="text-muted-foreground text-sm">
                No candidates found in the library for this missing book.
              </p>
            </div>
          ) : (
            <div className="space-y-3">
              <p className="text-muted-foreground text-xs">
                {rankedCandidates.length} candidate{rankedCandidates.length !== 1 ? "s" : ""} found,
                ranked by match quality.
              </p>

              {!confirming ? (
                rankedCandidates.map((c) => (
                  <div
                    key={c.audiobookId}
                    className="border-border bg-muted/30 hover:bg-muted/50 flex flex-col justify-between gap-3 rounded-md border p-3 transition-colors sm:flex-row sm:items-center"
                  >
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-1.5">
                        <span className="text-foreground font-semibold break-words">
                          {c.bookName ?? "Unknown"}
                        </span>
                        <Badge className={getMatchClassName(c)}>{getMatchLabel(c)}</Badge>
                        <span className="text-muted-foreground text-[11px]">
                          {Math.round(c.titleSimilarity * 100)}% title
                        </span>
                      </div>
                      <div className="text-muted-foreground text-[11px] break-words">
                        {(c.authors ?? []).length ? (c.authors ?? []).join(", ") : "Unknown author"}
                        {c.year ? ` · ${c.year}` : ""}
                      </div>
                      {(c.series ?? "").trim() ? (
                        <div className="text-muted-foreground text-[11px]">
                          Current series: {c.series}
                          {c.seriesPart ? <> · part {c.seriesPart}</> : null}
                        </div>
                      ) : null}
                    </div>
                    <Button
                      size="sm"
                      className="w-full self-end sm:w-auto sm:self-center"
                      onClick={() => {
                        handleSelect(c);
                      }}
                    >
                      <BookOpen className="mr-1 h-3.5 w-3.5" />
                      Select
                    </Button>
                  </div>
                ))
              ) : selectedCandidate ? (
                <div className="border-border bg-muted/40 space-y-3 rounded-lg border p-4">
                  <p className="text-muted-foreground text-xs">
                    Confirm to apply the following book to the missing slot:
                  </p>
                  <div className="text-foreground rounded-md border border-dashed p-2 text-sm">
                    {missingBook.position
                      ? `Position ${missingBook.position}`
                      : "Unnumbered position"}
                    {missingBook.title ? ` — "${missingBook.title}"` : ""}
                  </div>

                  <div className="bg-background rounded-md border p-3">
                    <div className="flex flex-wrap items-center gap-1.5">
                      <span className="text-foreground font-semibold break-words">
                        {selectedCandidate.bookName ?? "Unknown"}
                      </span>
                      <Badge className={getMatchClassName(selectedCandidate)}>
                        {getMatchLabel(selectedCandidate)}
                      </Badge>
                    </div>
                    <div className="text-muted-foreground text-[11px]">
                      {(selectedCandidate.authors ?? []).join(", ") || "Unknown author"}
                      {selectedCandidate.year ? ` · ${selectedCandidate.year}` : ""}
                    </div>
                    {(selectedCandidate.series ?? "").trim() ? (
                      <div className="text-muted-foreground text-[11px]">
                        Current series: {selectedCandidate.series}
                        {selectedCandidate.seriesPart ? (
                          <> · part {selectedCandidate.seriesPart}</>
                        ) : null}
                      </div>
                    ) : null}
                  </div>

                  <p className="text-sm">
                    This will set the series and part on the book, update its tags, and move it to
                    the correct library folder.
                  </p>
                </div>
              ) : null}
            </div>
          )}
        </div>

        <div className="border-border flex flex-col-reverse justify-end gap-2 border-t pt-4 sm:flex-row">
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            disabled={applying}
            onClick={() => {
              if (confirming) {
                handleBackToList();
              } else {
                handleClose();
              }
            }}
          >
            {confirming ? "Back" : "Cancel"}
          </Button>

          {confirming ? (
            <Button
              className="w-full sm:w-auto"
              disabled={applying || !selectedCandidate}
              onClick={() => {
                void handleConfirm();
              }}
            >
              {applying ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Applying...
                </>
              ) : (
                <>
                  <Check className="mr-2 h-4 w-4" />
                  Confirm
                </>
              )}
            </Button>
          ) : null}
        </div>
      </DialogContent>
    </Dialog>
  );
}

export default MissingBookCandidatesDialog;
