import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { ListChecks, Pencil, RefreshCw, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { OperationProgressBar } from "@/components/OperationProgressBar";
import { BulkBookEditDialog } from "./BulkBookEditDialog";
import { consistencyApi, metadataRefreshApi } from "@/services/api";
import { OperationKeys, SignalREvents } from "@/constants/signalrEvents";
import { useOperationResync } from "@/hooks/useOperationResync";
import { useSignalREvent } from "@/hooks/useSignalR";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
import type { BookSelection } from "@/hooks/useBookSelection";

const TITLE_PREVIEW_LIMIT = 3;

// Payloads mirror the backend records (BulkEditEvents.cs, MetadataRefreshEvents.cs,
// ConsistencyCheckProgress.cs / ConsistencyCheckComplete.cs) as camelCase JSON.
interface BulkEditProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface BulkEditCompletePayload {
  processed: number;
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

interface CheckProgressPayload {
  message: string;
  booksChecked: number;
  totalBooks: number;
  issuesFound: number;
  scope: "library" | "selected";
}

interface CheckCompletePayload {
  totalBooksChecked: number;
  totalIssuesFound: number;
  scope: "library" | "selected";
}

export interface BookBulkActionBarProps {
  selection: BookSelection;
}

/**
 * The multi-select action toolbar the four library book lists render above their rows. Returns
 * null when nothing is selected and no background operation is running, so it stays mounted (and
 * keeps its SignalR listeners and operation resync) across navigation while a bulk edit, a
 * selected metadata refresh, or a selected consistency check is in flight.
 */
export function BookBulkActionBar({ selection }: BookBulkActionBarProps) {
  const queryClient = useQueryClient();

  const [bulkEditProgress, setBulkEditProgress] = useState<BulkEditProgressPayload | null>(null);
  const [refreshProgress, setRefreshProgress] = useState<RefreshProgressPayload | null>(null);
  const [checkProgress, setCheckProgress] = useState<CheckProgressPayload | null>(null);
  const [bulkEditOpen, setBulkEditOpen] = useState(false);

  const anyOperationRunning =
    bulkEditProgress !== null || refreshProgress !== null || checkProgress !== null;

  const invalidateCommonViews = () => {
    void queryClient.invalidateQueries({ queryKey: ["books"] });
    void queryClient.invalidateQueries({ queryKey: ["author"] });
    void queryClient.invalidateQueries({ queryKey: ["seriesDetail"] });
    void queryClient.invalidateQueries({ queryKey: ["metadataRefresh"] });
  };

  useSignalREvent<BulkEditProgressPayload>(SignalREvents.BulkEditProgress, setBulkEditProgress);

  useSignalREvent<BulkEditCompletePayload>(SignalREvents.BulkEditComplete, (data) => {
    setBulkEditProgress(null);
    if (data.failed > 0) {
      toast.warning(`Bulk edit complete: ${data.succeeded} updated, ${data.failed} failed`);
    } else {
      toast.success(`Bulk edit complete: ${data.succeeded} updated`);
    }
    invalidateCommonViews();
    void queryClient.invalidateQueries({ queryKey: ["similarValueNames"] });
    // The applied edits changed the books; the selection that described them is stale now.
    selection.clear();
  });

  useSignalREvent<RefreshProgressPayload>(
    SignalREvents.MetadataRefreshProgress,
    setRefreshProgress,
  );

  useSignalREvent<RefreshCompletePayload>(SignalREvents.MetadataRefreshComplete, (data) => {
    setRefreshProgress(null);
    if (data.stopReason) {
      toast.warning(
        `${data.stopReason}. ${data.totalSucceeded} succeeded, ${data.totalFailed} failed.`,
      );
    } else if (data.totalFailed > 0) {
      toast.warning(
        `Metadata refresh complete: ${data.totalSucceeded} refreshed, ${data.totalFailed} failed`,
      );
    } else {
      toast.success(
        `Metadata refresh complete: ${data.totalSucceeded} refreshed, ${data.totalFailed} failed`,
      );
    }
    invalidateCommonViews();
  });

  useSignalREvent<CheckProgressPayload>(SignalREvents.ConsistencyCheckProgress, (data) => {
    if (data.scope !== "selected") return;
    setCheckProgress(data);
  });

  useSignalREvent<CheckCompletePayload>(SignalREvents.ConsistencyCheckComplete, (data) => {
    if (data.scope !== "selected") return;
    setCheckProgress(null);
    toast.success(
      `Check complete: ${data.totalBooksChecked} books checked, ${data.totalIssuesFound} issues found`,
    );
    invalidateCommonViews();
  });

  // Recover each in-flight operation (started here or elsewhere, or events missed while
  // disconnected) on mount and after a SignalR reconnect — the same pattern MetadataRefresh and
  // LibraryConsistency use for their own operations.
  useOperationResync(OperationKeys.bulkEdit, (status) => {
    if (status.isRunning) {
      setBulkEditProgress((prev) =>
        prev && prev.total > 0
          ? prev
          : { processed: status.processed, total: status.total, succeeded: 0, failed: 0 },
      );
    } else {
      setBulkEditProgress(null);
    }
  });

  useOperationResync(OperationKeys.metadataRefresh, (status) => {
    if (status.isRunning) {
      setRefreshProgress((prev) =>
        prev && prev.total > 0
          ? prev
          : { processed: status.processed, total: status.total, succeeded: 0, failed: 0 },
      );
    } else {
      setRefreshProgress(null);
    }
  });

  useOperationResync(OperationKeys.consistencyCheckSelected, (status) => {
    if (status.isRunning) {
      setCheckProgress(
        (prev) =>
          prev ?? {
            message: "Resuming check...",
            booksChecked: status.processed,
            totalBooks: status.total,
            issuesFound: 0,
            scope: "selected",
          },
      );
    } else {
      setCheckProgress(null);
    }
  });

  const handleRefresh = async () => {
    const ids = selection.selectedBooks.map((b) => b.id);
    try {
      await metadataRefreshApi.refreshSelected(ids);
      toast.success(`Refreshing metadata for ${selection.count} books…`);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    }
  };

  const handleCheck = async () => {
    const ids = selection.selectedBooks.map((b) => b.id);
    try {
      await consistencyApi.checkSelected(ids);
      toast.success(`Consistency check started for ${selection.count} books`);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    }
  };

  if (selection.count === 0 && !anyOperationRunning) {
    return null;
  }

  const previewTitles = selection.selectedBooks
    .slice(0, TITLE_PREVIEW_LIMIT)
    .map((b) => b.title)
    .filter(Boolean);
  const extraTitleCount = selection.count - previewTitles.length;

  return (
    <>
      <div className="border-border bg-muted/30 space-y-3 rounded-lg border p-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          {selection.count > 0 && (
            <div className="min-w-0 flex-1">
              <span className="text-foreground text-sm font-semibold">
                {selection.count} selected
              </span>
              {previewTitles.length > 0 && (
                <p className="text-muted-foreground mt-0.5 truncate text-xs">
                  {previewTitles.join(", ")}
                  {extraTitleCount > 0 ? `… and ${extraTitleCount} more` : ""}
                </p>
              )}
            </div>
          )}

          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={anyOperationRunning}
              onClick={() => void handleRefresh()}
            >
              <RefreshCw className="mr-1.5 h-3.5 w-3.5" />
              Refresh Metadata
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={anyOperationRunning}
              onClick={() => void handleCheck()}
            >
              <ListChecks className="mr-1.5 h-3.5 w-3.5" />
              Check Consistency
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={anyOperationRunning}
              onClick={() => setBulkEditOpen(true)}
            >
              <Pencil className="mr-1.5 h-3.5 w-3.5" />
              Edit Metadata
            </Button>
            <Button
              variant="ghost"
              size="sm"
              disabled={anyOperationRunning}
              onClick={() => selection.clear()}
            >
              <X className="mr-1.5 h-3.5 w-3.5" />
              Clear selection
            </Button>
          </div>
        </div>

        {anyOperationRunning && (
          <div className="space-y-2">
            {bulkEditProgress && (
              <OperationProgressBar
                compact
                processed={bulkEditProgress.processed}
                total={bulkEditProgress.total}
                label="Bulk editing books..."
                subText={`${bulkEditProgress.succeeded} updated, ${bulkEditProgress.failed} failed`}
              />
            )}
            {refreshProgress && (
              <OperationProgressBar
                compact
                processed={refreshProgress.processed}
                total={refreshProgress.total}
                label="Refreshing metadata..."
                subText={`${refreshProgress.succeeded} refreshed, ${refreshProgress.failed} failed`}
              />
            )}
            {checkProgress && (
              <OperationProgressBar
                compact
                processed={checkProgress.booksChecked}
                total={checkProgress.totalBooks}
                label={`${checkProgress.message || "Checking consistency..."} (${checkProgress.issuesFound} issues found)`}
              />
            )}
          </div>
        )}
      </div>

      <BulkBookEditDialog
        open={bulkEditOpen}
        onOpenChange={setBulkEditOpen}
        selectedBooks={selection.selectedBooks}
      />
    </>
  );
}

export default BookBulkActionBar;
