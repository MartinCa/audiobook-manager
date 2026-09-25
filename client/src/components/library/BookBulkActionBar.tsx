import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { ListChecks, Pencil, RefreshCw, Search, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { OperationProgressBar } from "@/components/OperationProgressBar";
import { BulkBookEditDialog } from "./BulkBookEditDialog";
import { BulkOnlineMatchSearchDialog } from "./BulkOnlineMatchSearchDialog";
import { consistencyApi, metadataRefreshApi, pendingOnlineMatchApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { OperationKeys, SignalREvents } from "@/constants/signalrEvents";
import { useOperationResync } from "@/hooks/useOperationResync";
import { useSignalREvent } from "@/hooks/useSignalR";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
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

interface OnlineMatchSearchProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface OnlineMatchSearchCompletePayload {
  totalProcessed: number;
  total: number;
  totalSucceeded: number;
  totalFailed: number;
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
  const [onlineMatchSearchProgress, setOnlineMatchSearchProgress] =
    useState<OnlineMatchSearchProgressPayload | null>(null);
  const [bulkEditOpen, setBulkEditOpen] = useState(false);
  const [onlineMatchSearchOpen, setOnlineMatchSearchOpen] = useState(false);

  const anyOperationRunning =
    bulkEditProgress !== null ||
    refreshProgress !== null ||
    checkProgress !== null ||
    onlineMatchSearchProgress !== null;

  const invalidateCommonViews = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.books.all() });
    void queryClient.invalidateQueries({ queryKey: queryKeys.author.all() });
    void queryClient.invalidateQueries({ queryKey: queryKeys.seriesDetail.all() });
    void queryClient.invalidateQueries({ queryKey: queryKeys.metadataRefresh.all() });
    // OwnedBookList's shared issue-count badge (Bug 8 unification): every owned-book list reads
    // this one cache entry, so it must be invalidated here too, not just the library list's own
    // page query as before.
    void queryClient.invalidateQueries({ queryKey: queryKeys.consistency.all() });
    // The Missing Tags page also renders through OwnedBookList: a bulk edit is precisely the
    // operation most likely to fill in the field(s) a book was shown for, so its page must be
    // invalidated too or the list keeps showing a now-fixed book until the fields/search change.
    void queryClient.invalidateQueries({ queryKey: queryKeys.missingTagsAudiobooks.all() });
  };

  // Recover each in-flight operation (started here or elsewhere, or events missed while
  // disconnected) on mount and after a SignalR reconnect — the same pattern MetadataRefresh and
  // LibraryConsistency use for their own operations. Each returned invalidate is called from the
  // matching operation's event handlers so a status response fetched before a real event is
  // discarded instead of clobbering the state the event set.
  const invalidateBulkEdit = useOperationResync(OperationKeys.bulkEdit, (status) => {
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

  const invalidateMetadataRefresh = useOperationResync(OperationKeys.metadataRefresh, (status) => {
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

  const invalidateCheckSelected = useOperationResync(
    OperationKeys.consistencyCheckSelected,
    (status) => {
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
    },
  );

  const invalidateOnlineMatchSearch = useOperationResync(
    OperationKeys.pendingOnlineMatchSearch,
    (status) => {
      if (status.isRunning) {
        setOnlineMatchSearchProgress((prev) =>
          prev && prev.total > 0
            ? prev
            : { processed: status.processed, total: status.total, succeeded: 0, failed: 0 },
        );
      } else {
        setOnlineMatchSearchProgress(null);
      }
    },
  );

  useSignalREvent<BulkEditProgressPayload>(SignalREvents.BulkEditProgress, (data) => {
    invalidateBulkEdit();
    setBulkEditProgress(data);
  });

  useSignalREvent<BulkEditCompletePayload>(SignalREvents.BulkEditComplete, (data) => {
    invalidateBulkEdit();
    setBulkEditProgress(null);
    if (data.failed > 0) {
      notifications.warning(`Bulk edit complete: ${data.succeeded} updated, ${data.failed} failed`);
    } else {
      notifications.success(`Bulk edit complete: ${data.succeeded} updated`);
    }
    invalidateCommonViews();
    // The applied edits changed the books; the selection that described them is stale now.
    selection.clear();
  });

  useSignalREvent<RefreshProgressPayload>(SignalREvents.MetadataRefreshProgress, (data) => {
    invalidateMetadataRefresh();
    setRefreshProgress(data);
  });

  useSignalREvent<RefreshCompletePayload>(SignalREvents.MetadataRefreshComplete, (data) => {
    invalidateMetadataRefresh();
    setRefreshProgress(null);
    if (data.stopReason) {
      notifications.warning(
        `${data.stopReason}. ${data.totalSucceeded} succeeded, ${data.totalFailed} failed.`,
      );
    } else if (data.totalFailed > 0) {
      notifications.warning(
        `Metadata refresh complete: ${data.totalSucceeded} refreshed, ${data.totalFailed} failed`,
      );
    } else {
      notifications.success(
        `Metadata refresh complete: ${data.totalSucceeded} refreshed, ${data.totalFailed} failed`,
      );
    }
    invalidateCommonViews();
  });

  useSignalREvent<CheckProgressPayload>(SignalREvents.ConsistencyCheckProgress, (data) => {
    if (data.scope !== "selected") return;
    invalidateCheckSelected();
    setCheckProgress(data);
  });

  useSignalREvent<CheckCompletePayload>(SignalREvents.ConsistencyCheckComplete, (data) => {
    if (data.scope !== "selected") return;
    invalidateCheckSelected();
    setCheckProgress(null);
    notifications.success(
      `Check complete: ${data.totalBooksChecked} books checked, ${data.totalIssuesFound} issues found`,
    );
    invalidateCommonViews();
  });

  useSignalREvent<OnlineMatchSearchProgressPayload>(
    SignalREvents.PendingOnlineMatchSearchProgress,
    (data) => {
      invalidateOnlineMatchSearch();
      setOnlineMatchSearchProgress(data);
    },
  );

  useSignalREvent<OnlineMatchSearchCompletePayload>(
    SignalREvents.PendingOnlineMatchSearchComplete,
    (data) => {
      invalidateOnlineMatchSearch();
      setOnlineMatchSearchProgress(null);
      if (data.totalFailed > 0) {
        notifications.warning(
          `Online metadata search complete: ${data.totalSucceeded} searched, ${data.totalFailed} failed`,
        );
      } else {
        notifications.success(
          `Online metadata search complete: ${data.totalSucceeded} searched`,
        );
      }
      // Books themselves are unchanged by a search - only the pending-match lists need to
      // refresh, not the book/author/series/consistency views invalidateCommonViews covers.
      void queryClient.invalidateQueries({ queryKey: queryKeys.pendingOnlineMatch.all() });
      selection.clear();
    },
  );

  const handleRefresh = async () => {
    const ids = selection.selectedBooks.map((b) => b.id);
    try {
      await metadataRefreshApi.refreshSelected(ids);
      notifications.success(`Refreshing metadata for ${selection.count} books…`);
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    }
  };

  const handleCheck = async () => {
    const ids = selection.selectedBooks.map((b) => b.id);
    try {
      await consistencyApi.checkSelected(ids);
      notifications.success(`Consistency check started for ${selection.count} books`);
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    }
  };

  const handleStartOnlineMatchSearch = async (sourceNames: string[]) => {
    const ids = selection.selectedBooks.map((b) => b.id);
    try {
      await pendingOnlineMatchApi.startSearchSelected(ids, sourceNames);
      notifications.success(`Searching online metadata for ${selection.count} books…`);
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
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
      <div className="border-border bg-muted/30 rounded-lg border p-3">
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
              variant="outline"
              size="sm"
              disabled={anyOperationRunning}
              onClick={() => setOnlineMatchSearchOpen(true)}
            >
              <Search className="mr-1.5 h-3.5 w-3.5" />
              Search Online Metadata
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
      </div>

      {anyOperationRunning && (
        <div
          role="status"
          aria-label="Background operation progress"
          className="bg-popover text-popover-foreground border-border fixed inset-x-3 bottom-3 z-50 space-y-2 rounded-lg border p-3 shadow-lg sm:inset-x-auto sm:right-auto sm:left-4 sm:w-96"
        >
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
          {onlineMatchSearchProgress && (
            <OperationProgressBar
              compact
              processed={onlineMatchSearchProgress.processed}
              total={onlineMatchSearchProgress.total}
              label="Searching online metadata..."
              subText={`${onlineMatchSearchProgress.succeeded} searched, ${onlineMatchSearchProgress.failed} failed`}
            />
          )}
        </div>
      )}

      <BulkBookEditDialog
        open={bulkEditOpen}
        onOpenChange={setBulkEditOpen}
        selectedBooks={selection.selectedBooks}
      />

      <BulkOnlineMatchSearchDialog
        open={onlineMatchSearchOpen}
        onOpenChange={setOnlineMatchSearchOpen}
        bookCount={selection.count}
        onConfirm={(sourceNames) => {
          void handleStartOnlineMatchSearch(sourceNames);
        }}
      />
    </>
  );
}

export default BookBulkActionBar;
