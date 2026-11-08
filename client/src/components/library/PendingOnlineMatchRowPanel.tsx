import { useMemo, useState } from "react";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { SourceTabbedResults } from "@/components/SourceTabbedResults";
import { pendingOnlineMatchApi } from "@/services/api";
import { pendingSnapshotToSearchResult } from "@/helpers/pendingMetadataRefresh";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type { PendingOnlineMatchListItem } from "@/types/PendingOnlineMatch";

export interface PendingOnlineMatchRowPanelProps {
  item: PendingOnlineMatchListItem;
  /**
   * Sends the request that starts the selection and resolves once the server has accepted it, at
   * which point the page collapses the row so the user can move on (it rejects if the server
   * refuses). The work (fetching the candidate, applying what the automated rules accept) continues
   * in the background; the page reports its outcome when SingleBookOperationComplete arrives. The
   * page tracks the book as busy from before the request goes out, which is why the request is
   * handed over rather than sent here.
   */
  start: (request: () => Promise<void>) => Promise<void>;
}

/**
 * A pending online-match row's expanded content: the candidate results, presented with
 * MetadataSearchResultCard - the same card the interactive "Search Online Metadata" dialog
 * renders, so a bulk-search candidate looks identical to one the user searched for by hand.
 * Selecting a candidate asks the server to fetch its full details and hand them to the normal
 * pending metadata-refresh review/apply flow (the book's own "Refresh Now" review banner) rather
 * than saving anything directly here - this row's job is choosing, not applying. The server runs
 * that in the background and answers right away, so the panel only waits for the request to be
 * accepted.
 */
export function PendingOnlineMatchRowPanel({ item, start }: PendingOnlineMatchRowPanelProps) {
  const [selectingIndex, setSelectingIndex] = useState<number | null>(null);

  const handleSelect = async (index: number) => {
    setSelectingIndex(index);
    try {
      await start(() => pendingOnlineMatchApi.selectResult(item.audiobookId, index));
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setSelectingIndex(null);
    }
  };

  // Converted once so the tab grouping sees stable objects; the index into this array is the
  // index the server selects by, which SourceTabbedResults hands back untouched.
  const results = useMemo(() => item.results.map(pendingSnapshotToSearchResult), [item.results]);

  if (item.results.length === 0) {
    return (
      <p className="text-muted-foreground py-4 text-center text-xs">
        No results found for this book.
      </p>
    );
  }

  return (
    <SourceTabbedResults
      results={results}
      sources={item.sourceNames}
      renderActions={(_result, idx) => (
        <Button
          disabled={selectingIndex !== null}
          onClick={() => {
            void handleSelect(idx);
          }}
          className="min-w-0 flex-1 sm:w-auto sm:flex-none"
        >
          {selectingIndex === idx ? <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" /> : null}
          Select this match
        </Button>
      )}
    />
  );
}

export default PendingOnlineMatchRowPanel;
