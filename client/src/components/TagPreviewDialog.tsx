import { useState, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { MetadataFieldDiffTable } from "@/components/MetadataFieldDiffTable";
import { settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useMetadataFieldDiffs } from "@/hooks/useMetadataFieldDiffs";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

interface TagPreviewDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  currentInput: OrganizeAudiobookInput;
  searchResult: MetadataSearchResult;
  onApply: (
    result: MetadataSearchResult,
    selectedFields: Set<string>,
    saveImmediately: boolean,
  ) => void;
  /**
   * Shows the "don't save automatically" opt-out checkbox. Only the interactive search-result
   * flow (BookEditForm's own "Search Online Metadata" dialog) offers this — the pending-refresh
   * snapshot flow already always auto-submits on apply, unaffected by this toggle.
   */
  showAutoSaveToggle?: boolean;
}

export function TagPreviewDialog({
  open,
  onOpenChange,
  currentInput,
  searchResult,
  onApply,
  showAutoSaveToggle = false,
}: TagPreviewDialogProps) {
  const { data: langData } = useQuery({
    queryKey: queryKeys.languages(),
    queryFn: () => settingsApi.getLanguages(),
    staleTime: Infinity,
  });

  const languages = useMemo(() => langData?.languages ?? [], [langData?.languages]);

  const fields = useMetadataFieldDiffs(currentInput, searchResult, languages);

  const changedFieldKeys = useMemo(
    () => fields.filter((f) => f.changed).map((f) => f.key),
    [fields],
  );

  const [selected, setSelected] = useState<Set<string>>(() => new Set());
  // Opt-out of auto-save, for the search-result flow only (see showAutoSaveToggle). Must default
  // off — and reset to off — for every new flow: it is never remembered across dialog opens, so
  // it is reset alongside `selected` whenever a new search result is shown, exactly like that
  // state already is.
  const [dontSaveAutomatically, setDontSaveAutomatically] = useState(false);

  // Update selected when fields change
  const [lastSearchResult, setLastSearchResult] = useState<MetadataSearchResult | null>(null);
  if (searchResult !== lastSearchResult) {
    setLastSearchResult(searchResult);
    setSelected(new Set(changedFieldKeys));
    setDontSaveAutomatically(false);
  }

  const toggleField = (key: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  };

  const toggleAll = () => {
    if (selected.size === changedFieldKeys.length) {
      setSelected(new Set());
    } else {
      setSelected(new Set(changedFieldKeys));
    }
  };

  const saveImmediately = !dontSaveAutomatically;

  const handleApplySelected = () => {
    onApply(searchResult, selected, saveImmediately);
    onOpenChange(false);
  };

  const handleApplyAll = () => {
    const allKeys = new Set(fields.map((f) => f.key));
    onApply(searchResult, allKeys, saveImmediately);
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[90dvh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-3xl sm:p-6">
        <DialogHeader>
          <DialogTitle>Metadata Preview & Diff</DialogTitle>
        </DialogHeader>

        <div className="flex-1 space-y-4 overflow-y-auto py-2 text-xs">
          <p className="text-muted-foreground">
            Review scraped metadata from{" "}
            <strong className="text-foreground">{searchResult.source}</strong>. Select which fields
            you want to update.
          </p>

          <MetadataFieldDiffTable
            fields={fields}
            selected={selected}
            onToggleField={toggleField}
            onToggleAll={toggleAll}
            changedFieldKeys={changedFieldKeys}
          />
        </div>

        {showAutoSaveToggle && (
          // A plain div, not a <label>: wrapping the Checkbox in a <label> makes its wrapped text
          // concatenate onto the aria-label instead of the aria-label standing alone, which broke
          // this control's accessible name. The div's own onClick keeps the "click the text to
          // toggle" behavior a <label> would have given for free.
          <div
            className="text-muted-foreground flex items-center gap-2 pt-2 text-xs"
            onClick={() => setDontSaveAutomatically((prev) => !prev)}
          >
            <Checkbox
              checked={dontSaveAutomatically}
              onCheckedChange={(next) => setDontSaveAutomatically(next === true)}
              onClick={(e) => e.stopPropagation()}
              aria-label="Don't save automatically"
            />
            <span>Don&apos;t save automatically — apply to the edit form for review instead</span>
          </div>
        )}

        <div className="border-border flex flex-col-reverse items-stretch justify-end gap-2 border-t pt-3 sm:flex-row sm:items-center sm:pt-4">
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            onClick={() => onOpenChange(false)}
          >
            Cancel
          </Button>
          <Button
            variant="outline"
            disabled={selected.size === 0}
            className="w-full sm:w-auto"
            onClick={handleApplySelected}
          >
            {showAutoSaveToggle && saveImmediately
              ? `Apply & Save Selected (${selected.size})`
              : `Apply Selected (${selected.size})`}
          </Button>
          <Button className="w-full sm:w-auto" onClick={handleApplyAll}>
            {showAutoSaveToggle && saveImmediately ? "Apply & Save All" : "Apply All"}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export default TagPreviewDialog;
