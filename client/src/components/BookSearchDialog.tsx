import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Search, Loader2, Check } from "lucide-react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { MetadataSourceSelector } from "@/components/MetadataSourceSelector";
import { MetadataSearchResultCard } from "@/components/MetadataSearchResultCard";
import { metadataSearchApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { handleApiError } from "@/lib/api";
import { useSelectedSearchSources } from "@/hooks/useSelectedSearchSources";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

interface BookSearchDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSelectResult: (result: MetadataSearchResult) => void;
  initialQuery?: string;
}

export function BookSearchDialog({
  open,
  onOpenChange,
  onSelectResult,
  initialQuery = "",
}: BookSearchDialogProps) {
  const [query, setQuery] = useState(initialQuery);
  const [prevInitialQuery, setPrevInitialQuery] = useState(initialQuery);
  if (initialQuery !== prevInitialQuery) {
    setPrevInitialQuery(initialQuery);
    setQuery(initialQuery);
  }

  const [results, setResults] = useState<MetadataSearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectingDetails, setSelectingDetails] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pendingSeriesChoice, setPendingSeriesChoice] = useState<MetadataSearchResult | null>(null);

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
    const current = activeSources;
    setSelectedSources(
      current.includes(sourceName)
        ? current.filter((s) => s !== sourceName)
        : [...current, sourceName],
    );
  };

  const handleSearch = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!query.trim() || activeSources.length === 0) return;

    setLoading(true);
    setError(null);
    setResults([]);

    try {
      const res = await metadataSearchApi.searchMultiple(activeSources, query.trim());
      setResults(res.results || []);
    } catch (err: unknown) {
      setError(handleApiError(err).message);
    } finally {
      setLoading(false);
    }
  };

  // A result with more than one candidate series can't be applied as-is: the caller
  // (BookEditForm) expects a single series, so the user picks which one applies first.
  const finishChoosing = (result: MetadataSearchResult) => {
    if (result.series && result.series.length > 1) {
      setPendingSeriesChoice(result);
      return;
    }
    onSelectResult(result);
    onOpenChange(false);
  };

  const handleChoose = async (item: MetadataSearchResult) => {
    if (item.url && (!item.authors?.length || !item.description)) {
      setSelectingDetails(item.url);
      try {
        const fullDetails = await metadataSearchApi.getBookDetails(item.url);
        finishChoosing(fullDetails);
      } catch {
        finishChoosing(item);
      } finally {
        setSelectingDetails(null);
      }
    } else {
      finishChoosing(item);
    }
  };

  const handleChooseSeries = (index: number) => {
    if (!pendingSeriesChoice) return;
    const chosen = pendingSeriesChoice.series[index];
    onSelectResult({ ...pendingSeriesChoice, series: chosen ? [chosen] : [] });
    setPendingSeriesChoice(null);
    onOpenChange(false);
  };

  const handleOpenChange = (next: boolean) => {
    if (!next) setPendingSeriesChoice(null);
    onOpenChange(next);
  };

  if (pendingSeriesChoice) {
    return (
      <Dialog open={open} onOpenChange={handleOpenChange}>
        {/* Scrollable-dialog shell (AGENTS.md): header, one flex-1 scroll body, footer after it.
            A single overflow-y-auto region keeps the table reachable on short screens and never
            nests dual scrollbars. The table itself handles only horizontal overflow. */}
        <DialogContent className="flex max-h-[90dvh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-lg sm:p-6">
          <DialogHeader>
            <DialogTitle>Select Series</DialogTitle>
          </DialogHeader>
          <div className="min-h-0 flex-1 space-y-3 overflow-y-auto">
            <p className="text-muted-foreground text-xs">
              This result matched more than one series. Choose which one applies to{" "}
              <strong>{pendingSeriesChoice.bookName}</strong>.
            </p>
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Series</TableHead>
                    <TableHead>Part</TableHead>
                    <TableHead className="w-10" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {pendingSeriesChoice.series.map((s, idx) => (
                    <TableRow key={`${s.seriesName}-${idx}`}>
                      <TableCell className="break-words">{s.seriesName}</TableCell>
                      <TableCell>{s.seriesPart}</TableCell>
                      <TableCell>
                        <Button size="sm" onClick={() => handleChooseSeries(idx)}>
                          <Check className="h-3.5 w-3.5" />
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </div>
          <div className="border-border flex justify-end border-t pt-4">
            <Button
              variant="outline"
              className="w-full sm:w-auto"
              onClick={() => setPendingSeriesChoice(null)}
            >
              Back to results
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="flex max-h-[90dvh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-3xl sm:p-6">
        <DialogHeader>
          <DialogTitle>Search Online Metadata</DialogTitle>
        </DialogHeader>

        <div className="flex min-h-0 min-w-0 flex-1 flex-col gap-4 py-2 text-xs">
          <form
            onSubmit={(e) => {
              // This dialog is opened from BookEditForm's own <form>. Its DialogContent
              // portals to document.body, but React still bubbles synthetic events through
              // the component tree rather than the DOM tree — so without stopping it here,
              // submitting this search form also submits (and saves/organizes) the outer one.
              e.stopPropagation();
              void handleSearch(e);
            }}
            className="flex gap-2"
          >
            <Input
              placeholder="Search title, author, or paste URL..."
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              className="min-w-0 flex-1 sm:text-sm"
            />
            <Button
              type="submit"
              disabled={loading || !query.trim() || activeSources.length === 0}
              className="shrink-0"
            >
              {loading ? (
                <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
              ) : (
                <Search className="mr-1.5 h-4 w-4" />
              )}
              Search
            </Button>
          </form>

          <MetadataSourceSelector
            services={services}
            activeSources={activeSources}
            onToggleSource={toggleSource}
          />

          <div className="min-h-0 min-w-0 flex-1 space-y-3 overflow-y-auto">
            {error && <p className="text-destructive text-xs">{error}</p>}

            {results.length > 0 && (
              <div className="text-muted-foreground text-xs font-semibold uppercase">
                Results ({results.length})
              </div>
            )}

            {loading && (
              <div className="text-muted-foreground flex items-center justify-center py-8 text-sm">
                <Loader2 className="text-primary mr-2 h-5 w-5 animate-spin" />
                Searching sources...
              </div>
            )}

            {!loading && results.length === 0 && query && (
              <div className="text-muted-foreground py-8 text-center text-sm">
                No results found. Try changing your search query or sources.
              </div>
            )}

            <div className="space-y-2">
              {results.map((result, idx) => {
                const isBusy = selectingDetails === result.url;
                return (
                  <MetadataSearchResultCard
                    key={`${result.source}-${result.bookName}-${idx}`}
                    result={result}
                    actions={
                      <Button
                        size="sm"
                        disabled={isBusy}
                        onClick={() => {
                          void handleChoose(result);
                        }}
                        className="min-w-0 flex-1 sm:w-auto"
                      >
                        {isBusy ? <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" /> : null}
                        Apply
                      </Button>
                    }
                  />
                );
              })}
            </div>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export default BookSearchDialog;
