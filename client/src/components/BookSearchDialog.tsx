import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Search, ExternalLink, Loader2, Check } from "lucide-react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { metadataSearchApi } from "@/services/api";
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
    queryKey: ["metadataServices"],
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
    if (result.series.length > 1) {
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
        <DialogContent className="max-w-lg">
          <DialogHeader>
            <DialogTitle>Select Series</DialogTitle>
          </DialogHeader>
          <p className="text-muted-foreground text-xs">
            This result matched more than one series. Choose which one applies to{" "}
            <strong>{pendingSeriesChoice.bookName}</strong>.
          </p>
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
                  <TableCell>{s.seriesName}</TableCell>
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
          <div className="border-border flex justify-end border-t pt-4">
            <Button variant="outline" onClick={() => setPendingSeriesChoice(null)}>
              Back to results
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="max-h-[90vh] max-w-3xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Search Online Metadata</DialogTitle>
        </DialogHeader>

        <div className="space-y-4 py-2">
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
              className="flex-1"
            />
            <Button type="submit" disabled={loading || !query.trim() || activeSources.length === 0}>
              {loading ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <Search className="mr-2 h-4 w-4" />
              )}
              Search
            </Button>
          </form>

          <div>
            <div className="text-muted-foreground mb-1.5 text-xs font-semibold uppercase">
              Metadata Sources
            </div>
            <div className="flex flex-wrap gap-2">
              {services.map((service) => {
                const isConfigured = service.enabled;
                const isSelected = activeSources.includes(service.name);

                return (
                  <Badge
                    key={service.name}
                    variant={isSelected ? "default" : isConfigured ? "outline" : "secondary"}
                    className={`cursor-pointer select-none ${
                      !isConfigured ? "cursor-not-allowed opacity-50" : "hover:bg-primary/90"
                    }`}
                    onClick={() => {
                      if (isConfigured) toggleSource(service.name);
                    }}
                  >
                    {service.name}
                    {!isConfigured && ` (${service.disabledReason || "Unavailable"})`}
                  </Badge>
                );
              })}
            </div>
          </div>

          {error && <p className="text-destructive text-xs">{error}</p>}

          <div className="space-y-3">
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

            <div className="max-h-96 space-y-2 overflow-y-auto">
              {results.map((result, idx) => {
                const isBusy = selectingDetails === result.url;
                return (
                  <div
                    key={`${result.source}-${result.bookName}-${idx}`}
                    className="border-border bg-card hover:bg-muted/50 flex items-start justify-between gap-4 rounded-lg border p-3 transition-colors"
                  >
                    <div className="flex gap-3">
                      {result.imageUrl && (
                        <img
                          src={metadataSearchApi.getProxyImageUrl(result.imageUrl)}
                          alt={result.bookName}
                          className="h-16 w-16 shrink-0 rounded object-cover shadow-sm"
                          onError={(e) => {
                            (e.currentTarget as HTMLElement).style.display = "none";
                          }}
                        />
                      )}
                      <div>
                        <div className="flex items-center gap-2">
                          <span className="text-foreground font-semibold">{result.bookName}</span>
                          <Badge variant="secondary" className="text-[10px]">
                            {result.source}
                          </Badge>
                          {result.year && (
                            <span className="text-muted-foreground text-xs">({result.year})</span>
                          )}
                        </div>

                        <div className="text-muted-foreground text-xs">
                          {result.authors && result.authors.length > 0 && (
                            <div>
                              By:{" "}
                              <span className="text-foreground font-medium">
                                {result.authors.map((a) => a.name).join(", ")}
                              </span>
                            </div>
                          )}
                          {result.narrators && result.narrators.length > 0 && (
                            <div>Narrated by: {result.narrators.map((n) => n.name).join(", ")}</div>
                          )}
                          {result.series?.[0] && (
                            <div>
                              Series: {result.series[0].seriesName}{" "}
                              {result.series[0].seriesPart && `#${result.series[0].seriesPart}`}
                            </div>
                          )}
                        </div>
                      </div>
                    </div>

                    <div className="flex shrink-0 flex-col items-end gap-2">
                      <Button
                        size="sm"
                        disabled={isBusy}
                        onClick={() => {
                          void handleChoose(result);
                        }}
                      >
                        {isBusy ? <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" /> : null}
                        Apply
                      </Button>
                      {result.url && (
                        <a
                          href={result.url}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="text-muted-foreground hover:text-foreground flex items-center text-[11px]"
                        >
                          <ExternalLink className="mr-1 h-3 w-3" />
                          View Source
                        </a>
                      )}
                    </div>
                  </div>
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
