import { useState, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { MetadataFieldDiffTable } from "@/components/MetadataFieldDiffTable";
import { settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useMetadataFieldDiffs } from "@/hooks/useMetadataFieldDiffs";
import { useBookQualifiers } from "@/hooks/useBookQualifiers";
import { PrimarySeriesChooser } from "@/components/PrimarySeriesChooser";
import { splitTitleOnColon } from "@/helpers/titleSplitter";
import {
  allSeries,
  canonicalizeSeries,
  chooseSeriesPrimary,
  currentSeriesSet,
  sourceSeriesEntries,
  withPrimarySeriesFirst,
} from "@/helpers/seriesRelations";
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
  const qualifierOptions = useBookQualifiers();

  // Opt-in toggle to split a scraped "Title: Subtitle" title at its first colon-space. Defaults
  // off — a scraped title is trusted as-is unless the user explicitly asks otherwise (see
  // helpers/titleSplitter.ts).
  const [splitTitleOnColonEnabled, setSplitTitleOnColonEnabled] = useState(false);

  // Which of the source's series becomes the book's primary one. Undefined = the default the
  // apply would pick (the book's current primary when the source still lists it); reset with the
  // rest of the per-flow state below.
  const [chosenPrimarySeries, setChosenPrimarySeries] = useState<string | undefined>(undefined);
  const currentSeriesEntries = useMemo(
    () =>
      allSeries(
        currentSeriesSet(
          currentInput.series,
          currentInput.seriesPart,
          currentInput.additionalSeries,
        ),
      ),
    [currentInput.series, currentInput.seriesPart, currentInput.additionalSeries],
  );
  const sourceSeries = useMemo(
    () => canonicalizeSeries(sourceSeriesEntries(searchResult.series)),
    [searchResult.series],
  );
  const effectivePrimarySeries = chooseSeriesPrimary(
    currentSeriesEntries,
    sourceSeries,
    chosenPrimarySeries,
  )?.name;

  const fields = useMetadataFieldDiffs(
    currentInput,
    searchResult,
    languages,
    splitTitleOnColonEnabled,
    chosenPrimarySeries,
    qualifierOptions,
  );

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
    setSplitTitleOnColonEnabled(false);
    setChosenPrimarySeries(undefined);
  }

  // Belt-and-suspenders reset alongside the identity check above: that check assumes every new
  // flow hands in a fresh searchResult object, which holds today (BookSearchDialog builds a new
  // one per search) but is not guaranteed forever - a future cache hit for an identical query
  // could hand back the same reference. Re-opening the dialog is a stronger, independent signal
  // that a new flow has started, so it resets the toggle too regardless of object identity.
  const [wasOpen, setWasOpen] = useState(open);
  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) {
      setDontSaveAutomatically(false);
      setSplitTitleOnColonEnabled(false);
      setChosenPrimarySeries(undefined);
    }
  }

  // Re-sync `selected` whenever the split toggle itself flips (not just on a new searchResult) -
  // flipping it can change which fields the diff table reports as changed (e.g. "subtitle"
  // becomes changed once a title gets split), and without this `selected` silently keeps
  // whatever it was computed from before the flip. Left unsynced, "Apply Selected" would then
  // drop the newly-split subtitle even though the table shows it as changed and selectable - the
  // client-side analog of the bug MetadataRefreshApplier had server-side. Resets to exactly the
  // new changed-field set rather than trying to preserve manual deselections across the flip;
  // toggling this option is itself a big enough change to the preview that resetting selection
  // is the simpler, safer behavior.
  const [lastSplitTitleOnColonEnabled, setLastSplitTitleOnColonEnabled] =
    useState(splitTitleOnColonEnabled);
  if (splitTitleOnColonEnabled !== lastSplitTitleOnColonEnabled) {
    setLastSplitTitleOnColonEnabled(splitTitleOnColonEnabled);
    setSelected(new Set(changedFieldKeys));
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

  // The consumer (BookEditForm's handleApplyPreviewedTags) reads bookName/subtitle straight off
  // whatever result onApply is called with, so the split-adjusted values have to be baked into a
  // shallow copy here rather than passed alongside as extra state - onApply's signature carries
  // no room for that, and this way the consumer needs no changes at all.
  //
  // The recovered subtitle only has somewhere to "come from" when "bookName" is also in `keys` -
  // otherwise handleApplyPreviewedTags leaves the form's book name untouched (still the full raw
  // title) while still writing the recovered tail into subtitle, duplicating the same text across
  // both fields instead of moving it. Mirrors the same guard in the backend's
  // MetadataRefreshApplier. handleApplyAll's `keys` always includes "bookName" (it's every field
  // key, changed or not), so this only bites Apply Selected after a manual bookName deselection.
  const applyAdjustedResult = (keys: Set<string>): MetadataSearchResult => {
    const originalSubtitleBlank = !searchResult.subtitle?.trim();
    const { bookName, subtitle } = splitTitleOnColon(
      searchResult.bookName ?? "",
      searchResult.subtitle,
      splitTitleOnColonEnabled,
    );
    const subtitleRecoveredBySplit = originalSubtitleBlank && Boolean(subtitle);
    const effectiveSubtitle =
      subtitleRecoveredBySplit && !keys.has("bookName") ? searchResult.subtitle : subtitle;
    // The series go out primary-first (then the rest in canonical order), so the consumer applies
    // "first = primary, the others = additional" without a signature of its own for the choice.
    return withPrimarySeriesFirst(
      { ...searchResult, bookName, subtitle: effectiveSubtitle ?? undefined },
      currentSeriesEntries,
      chosenPrimarySeries,
    );
  };

  const handleApplySelected = () => {
    onApply(applyAdjustedResult(selected), selected, saveImmediately);
    onOpenChange(false);
  };

  const handleApplyAll = () => {
    const allKeys = new Set(fields.map((f) => f.key));
    onApply(applyAdjustedResult(allKeys), allKeys, saveImmediately);
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

          <PrimarySeriesChooser
            options={sourceSeries.map((s) => s.name)}
            value={effectivePrimarySeries ?? ""}
            onChange={setChosenPrimarySeries}
            idPrefix="tag-preview"
          />
        </div>

        {
          // Not a <label>: wrapping the Checkbox in one makes its wrapped text concatenate onto
          // the aria-label instead of the aria-label standing alone (see the identical comment on
          // the auto-save toggle below). Always visible, unlike that toggle: this dialog is
          // shared by the search flow and possibly others, and the default (off) is safe
          // everywhere.
        }
        <div className="text-muted-foreground flex items-center gap-2 pt-2 text-xs">
          <Checkbox
            checked={splitTitleOnColonEnabled}
            onCheckedChange={(next) => setSplitTitleOnColonEnabled(next === true)}
            aria-label="Split title into book name and subtitle at first colon"
          />
          <button
            type="button"
            className="text-left hover:underline"
            onClick={() => setSplitTitleOnColonEnabled((prev) => !prev)}
          >
            Split title into book name + subtitle at first colon (e.g. &quot;Title: Subtitle&quot;)
          </button>
        </div>

        {showAutoSaveToggle && (
          // Not a <label>: wrapping the Checkbox in one makes its wrapped text concatenate onto
          // the aria-label instead of the aria-label standing alone, which broke this control's
          // accessible name (and a custom role="checkbox" span isn't a "labelable element" a
          // native <label> click would activate anyway). The description is instead its own
          // sibling <button>, keyboard-focusable and Enter/Space-activatable, toggling the same
          // state as the checkbox rather than only being clickable text.
          <div className="text-muted-foreground flex items-center gap-2 pt-2 text-xs">
            <Checkbox
              checked={dontSaveAutomatically}
              onCheckedChange={(next) => setDontSaveAutomatically(next === true)}
              aria-label="Don't save automatically"
            />
            <button
              type="button"
              className="text-left hover:underline"
              onClick={() => setDontSaveAutomatically((prev) => !prev)}
            >
              Don&apos;t save automatically — apply to the edit form for review instead
            </button>
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
