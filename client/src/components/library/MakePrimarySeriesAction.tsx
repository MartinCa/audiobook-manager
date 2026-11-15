import { useState } from "react";
import { Loader2, Star } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { bulkEditApi } from "@/services/api";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type { BookSelection } from "@/hooks/useBookSelection";

export interface MakePrimarySeriesActionProps {
  /** The series the page is showing: the one the selected books are made primary in. */
  seriesName: string;
  selection: BookSelection;
  /** True while another bulk operation runs (the bar's other buttons are disabled too). */
  disabled?: boolean;
}

/**
 * The series page's bulk action: make this series the primary one of the selected books. The
 * primary series is the one in a book's tags and library folder (Audiobookshelf reads it from
 * there), so a book that lists this series as an additional one is re-tagged and may move; its
 * previous primary becomes an additional series. It goes through the same bulk edit as the other
 * bar actions, so progress and the outcome toast come from the bar's own SignalR listeners.
 */
export function MakePrimarySeriesAction({
  seriesName,
  selection,
  disabled,
}: MakePrimarySeriesActionProps) {
  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const ids = selection.selectedBooks.map((b) => b.id);
  const count = ids.length;

  const handleConfirm = async () => {
    setSubmitting(true);
    try {
      await bulkEditApi.apply(ids, {
        audiobookIds: ids,
        primarySeries: { action: "set", value: seriesName },
      });
      notifications.success(
        `Making "${seriesName}" the primary series of ${count} ${count === 1 ? "book" : "books"}`,
      );
      setOpen(false);
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <Button variant="outline" disabled={disabled} onClick={() => setOpen(true)}>
        <Star className="mr-1.5 h-3.5 w-3.5" />
        Make primary series
      </Button>

      <Dialog open={open} onOpenChange={(next) => !submitting && setOpen(next)}>
        <DialogContent className="w-[calc(100vw-2rem)] p-4 sm:max-w-md sm:p-6">
          <DialogHeader>
            <DialogTitle>Make this the primary series?</DialogTitle>
            <DialogDescription>
              Make <span className="text-foreground font-medium">{seriesName}</span> the primary
              series of the {count} selected {count === 1 ? "book" : "books"}. Each book&apos;s
              current primary series becomes an additional series. The primary series is the one
              written to the m4b tags and used in the library folder, so these files will be
              re-tagged and may be moved. A book that is not in this series is left unchanged.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              className="w-full sm:w-auto"
              disabled={submitting}
              onClick={() => setOpen(false)}
            >
              Cancel
            </Button>
            <Button
              type="button"
              className="w-full sm:w-auto"
              disabled={submitting || count === 0}
              onClick={() => void handleConfirm()}
            >
              {submitting ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Applying...
                </>
              ) : (
                "Make primary"
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

export default MakePrimarySeriesAction;
