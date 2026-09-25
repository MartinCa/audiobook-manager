import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { MetadataSearchResultCard } from "@/components/MetadataSearchResultCard";
import { pendingOnlineMatchApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { pendingSnapshotToSearchResult } from "@/helpers/pendingMetadataRefresh";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type { PendingOnlineMatchListItem } from "@/types/PendingOnlineMatch";

export interface PendingOnlineMatchRowPanelProps {
  item: PendingOnlineMatchListItem;
  onResolved: () => void;
}

/**
 * A pending online-match row's expanded content: the candidate results, presented with
 * MetadataSearchResultCard - the same card the interactive "Search Online Metadata" dialog
 * renders, so a bulk-search candidate looks identical to one the user searched for by hand.
 * Selecting a candidate fetches its full details and hands them to the normal pending
 * metadata-refresh review/apply flow (the book's own "Refresh Now" review banner) rather than
 * saving anything directly here - this row's job is choosing, not applying.
 */
export function PendingOnlineMatchRowPanel({ item, onResolved }: PendingOnlineMatchRowPanelProps) {
  const queryClient = useQueryClient();
  const [selectingIndex, setSelectingIndex] = useState<number | null>(null);

  const invalidateViews = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.pendingOnlineMatch.all() });
    void queryClient.invalidateQueries({ queryKey: queryKeys.metadataRefresh.all() });
    void queryClient.invalidateQueries({ queryKey: queryKeys.books.all() });
  };

  const handleSelect = async (index: number) => {
    setSelectingIndex(index);
    try {
      await pendingOnlineMatchApi.selectResult(item.audiobookId, index);
      notifications.success(
        "Match selected — review and save the changes from the book's pending metadata banner.",
      );
      invalidateViews();
      onResolved();
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setSelectingIndex(null);
    }
  };

  if (item.results.length === 0) {
    return (
      <p className="text-muted-foreground py-4 text-center text-xs">
        No results found for this book.
      </p>
    );
  }

  return (
    <div className="space-y-2">
      {item.results.map((snapshot, idx) => {
        const result = pendingSnapshotToSearchResult(snapshot);
        const isBusy = selectingIndex === idx;
        return (
          <MetadataSearchResultCard
            key={`${snapshot.source}-${snapshot.bookName}-${idx}`}
            result={result}
            actions={
              <Button
                size="sm"
                disabled={selectingIndex !== null}
                onClick={() => {
                  void handleSelect(idx);
                }}
                className="min-w-0 flex-1 sm:w-auto"
              >
                {isBusy ? <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" /> : null}
                Select this match
              </Button>
            }
          />
        );
      })}
    </div>
  );
}

export default PendingOnlineMatchRowPanel;
