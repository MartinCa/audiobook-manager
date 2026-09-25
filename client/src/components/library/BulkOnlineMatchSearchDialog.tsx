import { useQuery } from "@tanstack/react-query";
import { Search } from "lucide-react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { MetadataSourceSelector } from "@/components/MetadataSourceSelector";
import { metadataSearchApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useSelectedSearchSources } from "@/hooks/useSelectedSearchSources";

export interface BulkOnlineMatchSearchDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  bookCount: number;
  onConfirm: (sourceNames: string[]) => void;
}

/**
 * The source picker for the bulk "Search Online Metadata" action - reuses the same
 * MetadataSourceSelector and remembered-selection hook BookSearchDialog's interactive search
 * uses, so the two pickers never drift. The query each selected book is searched with is the
 * default one (book title, falling back to the file name) computed server-side by
 * PendingOnlineMatchService - this dialog only chooses the sources.
 */
export function BulkOnlineMatchSearchDialog({
  open,
  onOpenChange,
  bookCount,
  onConfirm,
}: BulkOnlineMatchSearchDialogProps) {
  const { data: services = [] } = useQuery({
    queryKey: queryKeys.metadataServices(),
    queryFn: () => metadataSearchApi.getServices(),
    enabled: open,
  });

  const [selectedSources, setSelectedSources] = useSelectedSearchSources(services);

  const activeSources =
    selectedSources.length > 0
      ? selectedSources
      : services.filter((s) => s.enabled).map((s) => s.name);

  const toggleSource = (sourceName: string) => {
    setSelectedSources(
      activeSources.includes(sourceName)
        ? activeSources.filter((s) => s !== sourceName)
        : [...activeSources, sourceName],
    );
  };

  const handleConfirm = () => {
    onConfirm(activeSources);
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[90dvh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-lg sm:p-6">
        <DialogHeader>
          <DialogTitle>Search Online Metadata</DialogTitle>
        </DialogHeader>

        <div className="space-y-4 py-2 text-xs">
          <p className="text-muted-foreground">
            Searches {bookCount} selected {bookCount === 1 ? "book" : "books"} across the chosen
            sources in the background. Review the results on the Pending Online Matches page once it
            finishes.
          </p>

          <MetadataSourceSelector
            services={services}
            activeSources={activeSources}
            onToggleSource={toggleSource}
          />
        </div>

        <div className="border-border flex flex-col-reverse justify-end gap-2 border-t pt-4 sm:flex-row sm:items-center">
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            onClick={() => onOpenChange(false)}
          >
            Cancel
          </Button>
          <Button
            className="w-full sm:w-auto"
            disabled={activeSources.length === 0}
            onClick={handleConfirm}
          >
            <Search className="mr-1.5 h-4 w-4" />
            Start Search
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export default BulkOnlineMatchSearchDialog;
