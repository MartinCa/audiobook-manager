import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, Trash2, ListX } from "lucide-react";
import { AppDialog } from "@/components/AppDialog";
import { Button } from "@/components/ui/button";
import { similarValuesApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";

interface IgnoredSimilarValuesDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  valueType: "author" | "series";
}

/**
 * Lists the pairs a user has explicitly marked as not similar for one tab (author/series), with
 * a way to remove one - which lets the grouping consider that pair again. Naturally small (one
 * row per manual ignore), so it is loaded unpaged, same as the rest of this resource's small
 * per-kind lists.
 */
export function IgnoredSimilarValuesDialog({
  open,
  onOpenChange,
  valueType,
}: IgnoredSimilarValuesDialogProps) {
  const queryClient = useQueryClient();
  const [removingId, setRemovingId] = useState<number | null>(null);

  const { data: pairs, isLoading } = useQuery({
    queryKey: queryKeys.similarValues.ignoredPairs(valueType),
    queryFn: () => similarValuesApi.getIgnoredPairs(valueType),
    enabled: open,
  });

  const handleRemove = async (id: number) => {
    setRemovingId(id);
    try {
      await similarValuesApi.removeIgnoredPair(valueType, id);
      // Removing an ignored pair can let the two values re-cluster, so the detected groups must
      // be re-split too - not just this dialog's own list.
      void queryClient.invalidateQueries({ queryKey: queryKeys.similarValues.all() });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setRemovingId(null);
    }
  };

  return (
    <AppDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Ignored Pairs"
      description={`Values marked as not similar for ${valueType === "author" ? "authors" : "series"}. Removing a pair lets those two values be grouped together again.`}
      contentClassName="sm:max-w-md"
    >
      {isLoading ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
          <Loader2 className="mb-2 h-6 w-6 animate-spin" />
          <p className="text-sm">Loading ignored pairs...</p>
        </div>
      ) : !pairs || pairs.length === 0 ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-8 text-center">
          <ListX className="mb-2 h-8 w-8 opacity-40" />
          <p className="text-sm">No ignored pairs yet.</p>
        </div>
      ) : (
        <ul className="space-y-2">
          {pairs.map((pair) => (
            <li
              key={pair.id}
              className="border-border bg-muted/30 flex items-center justify-between gap-2 rounded-md border p-2.5 text-xs"
            >
              <div className="min-w-0">
                <div className="text-foreground font-medium break-words">{pair.valueA}</div>
                <div className="text-muted-foreground break-words">{pair.valueB}</div>
              </div>
              <Button
                size="icon-sm"
                variant="ghost"
                aria-label="Remove ignored pair"
                disabled={removingId === pair.id}
                onClick={() => {
                  void handleRemove(pair.id);
                }}
              >
                {removingId === pair.id ? (
                  <Loader2 className="h-4 w-4 animate-spin" />
                ) : (
                  <Trash2 className="h-4 w-4" />
                )}
              </Button>
            </li>
          ))}
        </ul>
      )}
    </AppDialog>
  );
}

export default IgnoredSimilarValuesDialog;
