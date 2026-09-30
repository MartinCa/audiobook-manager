import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { MetadataFieldDiffTable } from "@/components/MetadataFieldDiffTable";
import { browseApi, metadataRefreshApi, settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { toAudiobook } from "@/helpers/audiobookMapping";
import { audiobookToOrganizeInput } from "@/helpers/organizeInput";
import { useBookQualifiers } from "@/hooks/useBookQualifiers";
import { pendingSnapshotToSearchResult } from "@/helpers/pendingMetadataRefresh";
import { useMetadataFieldDiffs } from "@/hooks/useMetadataFieldDiffs";
import { PrimarySeriesChooser } from "@/components/PrimarySeriesChooser";
import {
  allSeries,
  canonicalizeSeries,
  chooseSeriesPrimary,
  currentSeriesSet,
  sourceSeriesEntries,
} from "@/helpers/seriesRelations";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";

/** Maps a diff row's client-side key to the backend field name(s) MetadataRefreshFields defines; "series" is the whole set of a book's series with their parts and the primary. */
const CLIENT_KEY_TO_BACKEND_FIELDS: Record<string, string[]> = {
  authors: ["Authors"],
  narrators: ["Narrators"],
  bookName: ["BookName"],
  subtitle: ["Subtitle"],
  series: ["Series"],
  year: ["Year"],
  genres: ["Genres"],
  description: ["Description"],
  rating: ["Rating"],
  publisher: ["Publisher"],
  language: ["Language"],
  copyright: ["Copyright"],
  asin: ["Asin"],
  www: ["Www"],
  qualifiers: ["Qualifiers"],
};

interface PendingRefreshRowPanelProps {
  audiobookId: number;
  /** Called once the apply (or a dismiss elsewhere) has gone through, so the row can collapse and its row-level data can be dropped from the list. */
  onApplied: () => void;
}

/**
 * The metadata-refresh list's per-row quick apply: an inline, changed-fields-only rendering of
 * the same diff grid TagPreviewDialog shows in a modal (see MetadataFieldDiffTable/
 * useMetadataFieldDiffs), so a user reviewing the bulk list can approve one book without leaving
 * the page. Applying goes straight through the single-book apply endpoint - there is no mounted
 * edit form here for BookEditForm's own apply flow to route through.
 */
export function PendingRefreshRowPanel({ audiobookId, onApplied }: PendingRefreshRowPanelProps) {
  const queryClient = useQueryClient();
  const [applying, setApplying] = useState(false);
  // Opt-in toggle to split a scraped "Title: Subtitle" title at its first colon-space. Defaults
  // off (see helpers/titleSplitter.ts); no need to persist across anything since this component
  // remounts per row.
  const [splitTitleOnColonEnabled, setSplitTitleOnColonEnabled] = useState(false);
  // Which of the source's series becomes the book's primary one; undefined = the default the
  // server would pick (the book's current primary when the source still lists it).
  const [chosenPrimarySeries, setChosenPrimarySeries] = useState<string | undefined>(undefined);

  const { data: bookDetail, isLoading: loadingBook } = useQuery({
    queryKey: queryKeys.bookDetailRecord(audiobookId),
    queryFn: () => browseApi.getAudiobookDetail(audiobookId),
  });

  const { data: pending, isLoading: loadingPending } = useQuery({
    queryKey: queryKeys.metadataRefresh.pendingForBook(audiobookId),
    queryFn: () => metadataRefreshApi.getPendingForAudiobook(audiobookId).then((p) => p ?? null),
  });

  const { data: langData } = useQuery({
    queryKey: queryKeys.languages(),
    queryFn: () => settingsApi.getLanguages(),
    staleTime: Infinity,
  });
  const languages = useMemo(() => langData?.languages ?? [], [langData?.languages]);
  const qualifierOptions = useBookQualifiers();

  const currentInput = useMemo(
    () => (bookDetail ? audiobookToOrganizeInput(toAudiobook(bookDetail)) : {}),
    [bookDetail],
  );
  const searchResult = useMemo(
    () => (pending?.payload ? pendingSnapshotToSearchResult(pending.payload) : null),
    [pending],
  );

  const allFields = useMetadataFieldDiffs(
    currentInput,
    searchResult ?? {
      url: "",
      cleanUrl: "",
      source: "",
      authors: [],
      narrators: [],
      bookName: "",
      genres: [],
      series: [],
    },
    languages,
    splitTitleOnColonEnabled,
    chosenPrimarySeries,
    qualifierOptions,
  );

  const sourceSeries = useMemo(
    () => canonicalizeSeries(sourceSeriesEntries(searchResult?.series)),
    [searchResult],
  );
  const effectivePrimarySeries = chooseSeriesPrimary(
    allSeries(
      currentSeriesSet(currentInput.series, currentInput.seriesPart, currentInput.additionalSeries),
    ),
    sourceSeries,
    chosenPrimarySeries,
  )?.name;

  // Only fields the backend can actually apply, and only the ones that changed - "cover" has no
  // backend counterpart in the stored snapshot (see MetadataRefreshApplier), and an unchanged
  // field has nothing to offer here regardless.
  const changedFields = useMemo(
    () => allFields.filter((f) => f.changed && CLIENT_KEY_TO_BACKEND_FIELDS[f.key]),
    [allFields],
  );
  const changedFieldKeys = useMemo(() => changedFields.map((f) => f.key), [changedFields]);

  const [selected, setSelected] = useState<Set<string>>(() => new Set());
  const [lastKeys, setLastKeys] = useState<string[] | null>(null);
  // Gated on bookDetail too, not just searchResult: the pending-snapshot query can resolve
  // before the book-detail query does, and computing the diff against currentInput={} (still
  // unloaded) would flag every snapshot field as "changed" - freezing that over-inclusive set
  // into `selected` the moment lastKeys stops being null, with no later re-sync.
  if (bookDetail && searchResult && lastKeys === null) {
    setLastKeys(changedFieldKeys);
    setSelected(new Set(changedFieldKeys));
  }

  const toggleField = (key: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  const toggleAll = () => {
    setSelected((prev) =>
      prev.size === changedFieldKeys.length ? new Set() : new Set(changedFieldKeys),
    );
  };

  const backendFieldsFor = (keys: Iterable<string>): string[] => {
    const fields = new Set<string>();
    for (const key of keys) {
      for (const backendField of CLIENT_KEY_TO_BACKEND_FIELDS[key] ?? []) {
        fields.add(backendField);
      }
    }
    return Array.from(fields);
  };

  const apply = async (keys: Iterable<string>) => {
    setApplying(true);
    try {
      await metadataRefreshApi.applyPending(
        audiobookId,
        backendFieldsFor(keys),
        splitTitleOnColonEnabled,
        chosenPrimarySeries,
      );
      notifications.success("Metadata changes applied");
      void queryClient.invalidateQueries({ queryKey: queryKeys.metadataRefresh.all() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.bookDetail(audiobookId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.books.all() });
      onApplied();
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setApplying(false);
    }
  };

  if (loadingBook || loadingPending) {
    return (
      <div className="text-muted-foreground flex items-center justify-center gap-2 py-8 text-sm">
        <Loader2 className="h-4 w-4 animate-spin" />
        Loading changes...
      </div>
    );
  }

  if (!searchResult || changedFields.length === 0) {
    return (
      <div className="text-muted-foreground flex flex-col items-center gap-2 py-8 text-sm">
        <CheckCircle2 className="h-5 w-5" />
        No applicable pending changes for this book.
      </div>
    );
  }

  return (
    <div className="space-y-3 p-1">
      <MetadataFieldDiffTable
        fields={changedFields}
        selected={selected}
        onToggleField={toggleField}
        onToggleAll={toggleAll}
        changedFieldKeys={changedFieldKeys}
      />
      {changedFieldKeys.includes("series") && (
        <PrimarySeriesChooser
          options={sourceSeries.map((s) => s.name)}
          value={effectivePrimarySeries ?? ""}
          onChange={setChosenPrimarySeries}
          idPrefix={`pending-refresh-${audiobookId}`}
        />
      )}
      {
        // Not a <label>: wrapping the Checkbox in one makes its wrapped text concatenate onto
        // the aria-label instead of the aria-label standing alone (see the same pattern in
        // TagPreviewDialog).
      }
      <div className="text-muted-foreground flex items-center gap-2 text-xs">
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

      <div className="flex flex-col-reverse items-stretch justify-end gap-2 sm:flex-row sm:items-center">
        <Button
          variant="outline"
          size="sm"
          disabled={applying || selected.size === 0}
          onClick={() => void apply(selected)}
        >
          {applying ? <Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" /> : null}
          Apply Selected ({selected.size})
        </Button>
        <Button size="sm" disabled={applying} onClick={() => void apply(changedFieldKeys)}>
          {applying ? <Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" /> : null}
          Apply All
        </Button>
      </div>
    </div>
  );
}

export default PendingRefreshRowPanel;
