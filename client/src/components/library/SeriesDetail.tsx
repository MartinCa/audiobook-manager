import { useState } from "react";
import { Link, useNavigate } from "@tanstack/react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft,
  RefreshCw,
  Link as LinkIcon,
  ExternalLink,
  Loader2,
  ChevronRight,
  BookOpen,
  EyeOff,
  Eye,
  Search,
  Check,
  CheckCircle2,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { seriesApi } from "@/services/api";
import { useSignalREvent } from "@/hooks/useSignalR";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
import type { SeriesMatchCandidate } from "@/types/Series";
import { Route } from "@/routes/library/series/$seriesName";

interface SeriesRefreshCompletePayload {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
  stopReason?: string;
}

export function SeriesDetail() {
  const { seriesName } = Route.useParams();
  const { authorId } = Route.useSearch();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const decodedSeriesName = decodeURIComponent(seriesName || "");

  const [refreshing, setRefreshing] = useState(false);
  const [loadingCandidates, setLoadingCandidates] = useState(false);
  const [searchingCandidates, setSearchingCandidates] = useState(false);
  const [manualQuery, setManualQuery] = useState("");
  const [candidates, setCandidates] = useState<SeriesMatchCandidate[]>([]);
  const [candidatesLoaded, setCandidatesLoaded] = useState(false);
  const [matchingCandidate, setMatchingCandidate] = useState(false);
  const [updatingOmnibus, setUpdatingOmnibus] = useState(false);
  const [ignoringBookId, setIgnoringBookId] = useState<number | null>(null);

  const { data: detail, isLoading: loading } = useQuery({
    queryKey: ["seriesDetail", decodedSeriesName, authorId],
    queryFn: () => seriesApi.getSeriesDetail(decodedSeriesName),
    enabled: Boolean(decodedSeriesName),
  });

  useSignalREvent<SeriesRefreshCompletePayload>("SeriesRefreshComplete", (arg) => {
    setRefreshing(false);
    const msg = arg.stopReason
      ? `Refresh stopped: ${arg.stopReason}`
      : arg.totalFailed > 0
        ? `Refresh finished with ${arg.totalFailed} failure(s)`
        : "Refresh complete";
    toast.success(msg);
    void queryClient.invalidateQueries({
      queryKey: ["seriesDetail", decodedSeriesName, authorId],
    });
  });

  const handleRefresh = async () => {
    setRefreshing(true);
    try {
      await seriesApi.startRefresh(decodedSeriesName);
      toast.success("Series refresh queued");
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setRefreshing(false);
    }
  };

  const handleLoadCandidates = async () => {
    setLoadingCandidates(true);
    try {
      const results = await seriesApi.getMatchCandidates(decodedSeriesName);
      setCandidates(results);
      setCandidatesLoaded(true);
      if (results.length === 0) {
        toast.info("No candidates found automatically. Try searching manually.");
      }
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setLoadingCandidates(false);
    }
  };

  const handleSearchManualCandidates = async () => {
    const q = manualQuery.trim();
    if (!q) return;
    setSearchingCandidates(true);
    try {
      const results = await seriesApi.searchMatchCandidates(decodedSeriesName, q);
      setCandidates(results);
      setCandidatesLoaded(true);
      if (results.length === 0) {
        toast.info("No candidates found for that query.");
      }
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setSearchingCandidates(false);
    }
  };

  const handleApplyMatch = async (candidate: SeriesMatchCandidate) => {
    setMatchingCandidate(true);
    try {
      await seriesApi.matchSeries(
        decodedSeriesName,
        candidate.sourceName,
        candidate.sourceId,
        candidate.confidence,
        detail?.overview.includeOmnibusEditions,
      );
      setCandidates([]);
      setCandidatesLoaded(false);
      toast.success(`Matched to ${candidate.seriesName} (${candidate.sourceName})`);
      void queryClient.invalidateQueries({
        queryKey: ["seriesDetail", decodedSeriesName, authorId],
      });
      void queryClient.invalidateQueries({ queryKey: ["series"] });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setMatchingCandidate(false);
    }
  };

  const handleToggleOmnibus = async (checked: boolean) => {
    setUpdatingOmnibus(true);
    try {
      await seriesApi.setIncludeOmnibusEditions(decodedSeriesName, checked);
      toast.success(checked ? "Omnibus editions included" : "Omnibus editions excluded");
      void queryClient.invalidateQueries({
        queryKey: ["seriesDetail", decodedSeriesName, authorId],
      });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setUpdatingOmnibus(false);
    }
  };

  const handleSetIgnored = async (
    book: { id: number; position?: string | null; title?: string | null },
    ignored: boolean,
  ) => {
    setIgnoringBookId(book.id);
    try {
      if (ignored) {
        await seriesApi.ignoreExpectedBook(decodedSeriesName, book.position, book.title);
        toast.success(`Ignored "${book.title || "book"}"`);
      } else {
        await seriesApi.unignoreExpectedBook(decodedSeriesName, book.position, book.title);
        toast.success(`Unignored "${book.title || "book"}"`);
      }
      void queryClient.invalidateQueries({
        queryKey: ["seriesDetail", decodedSeriesName, authorId],
      });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setIgnoringBookId(null);
    }
  };

  if (loading) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-20">
        <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
        <p className="text-sm">Loading series details...</p>
      </div>
    );
  }

  if (!detail) {
    return (
      <div className="space-y-4 py-12 text-center">
        <h2 className="text-xl font-bold">Series not found</h2>
        <Button render={<Link to="/library/series" />}>Back to Series</Button>
      </div>
    );
  }

  const { overview, ownedBooks, missingBooks, ignoredBooks } = detail;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        {authorId ? (
          <Button
            variant="ghost"
            size="sm"
            className="w-full justify-start sm:w-auto"
            render={
              <Link to="/library/authors/$authorId" params={{ authorId: String(authorId) }} />
            }
          >
            <ArrowLeft className="mr-2 h-4 w-4" />
            Back to Author
          </Button>
        ) : (
          <Button
            variant="ghost"
            size="sm"
            className="w-full justify-start sm:w-auto"
            render={<Link to="/library/series" />}
          >
            <ArrowLeft className="mr-2 h-4 w-4" />
            Back to Series
          </Button>
        )}

        <div className="flex w-full flex-wrap items-center gap-2 sm:w-auto">
          <Button
            variant="outline"
            size="sm"
            className="w-full sm:w-auto"
            disabled={loadingCandidates || matchingCandidate}
            onClick={() => {
              void handleLoadCandidates();
            }}
          >
            {loadingCandidates ? (
              <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
            ) : (
              <LinkIcon className="mr-1.5 h-4 w-4" />
            )}
            {overview.isMatched ? "Re-match to Source" : "Match to Source"}
          </Button>

          {overview.isMatched && (
            <Button
              variant="outline"
              size="sm"
              className="w-full sm:w-auto"
              disabled={refreshing}
              onClick={() => {
                void handleRefresh();
              }}
            >
              <RefreshCw className={`mr-1.5 h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
              Refresh Online
            </Button>
          )}
        </div>
      </div>

      <div className="border-border border-b pb-4">
        <h1 className="text-foreground text-2xl font-bold break-words">{decodedSeriesName}</h1>
        <div className="text-muted-foreground flex flex-wrap items-center gap-2 text-sm">
          <span>
            {ownedBooks.length} {ownedBooks.length === 1 ? "book" : "books"} owned
          </span>
          {overview.isMatched && overview.missingBookCount > 0 && (
            <span className="font-semibold text-amber-600 dark:text-amber-400">
              &middot; {overview.missingBookCount} missing
            </span>
          )}
        </div>
      </div>

      <Card>
        <CardHeader className="py-3">
          <CardTitle className="text-muted-foreground text-sm font-semibold uppercase">
            Metadata Provider Match
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-4 text-xs">
          {overview.isMatched ? (
            <div className="flex flex-wrap items-center gap-3">
              <Badge
                variant="secondary"
                className="bg-emerald-500/15 text-emerald-600 dark:text-emerald-400"
              >
                <CheckCircle2 className="mr-1 h-3.5 w-3.5" />
                Matched to {overview.matchedSourceName}
              </Badge>
              {overview.matchedSourceUrl && (
                <a
                  href={overview.matchedSourceUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-primary flex items-center hover:underline"
                >
                  <ExternalLink className="mr-1 h-3 w-3" />
                  View at source
                </a>
              )}
              {overview.matchConfidence != null && (
                <span className="text-muted-foreground">
                  Confidence: {Math.round(overview.matchConfidence * 100)}%
                </span>
              )}
              {overview.lastRefreshedAt && (
                <span className="text-muted-foreground">
                  Last refreshed: {new Date(overview.lastRefreshedAt).toLocaleDateString()}
                </span>
              )}
            </div>
          ) : (
            <p className="text-muted-foreground">
              Not matched to an online metadata provider yet. Click "Match to Source" or search
              below to associate this series.
            </p>
          )}

          <div className="flex items-center space-x-2 pt-1">
            <Checkbox
              id="includeOmnibus"
              checked={overview.includeOmnibusEditions}
              disabled={updatingOmnibus}
              onCheckedChange={(checked) => {
                void handleToggleOmnibus(Boolean(checked));
              }}
            />
            <label
              htmlFor="includeOmnibus"
              className="text-muted-foreground cursor-pointer text-xs leading-none select-none"
            >
              Include omnibus/box-set editions in missing books list
            </label>
          </div>

          <div className="border-border space-y-2 border-t pt-3">
            <label className="text-muted-foreground font-semibold">
              Search title/author or paste a series URL
            </label>
            <div className="flex max-w-xl flex-col gap-2 sm:flex-row">
              <Input
                placeholder="e.g. Harry Potter, or https://hardcover.app/series/..."
                value={manualQuery}
                onChange={(e) => setManualQuery(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault();
                    void handleSearchManualCandidates();
                  }
                }}
                className="h-8 flex-1 text-xs"
              />
              <Button
                variant="secondary"
                size="sm"
                className="h-8 w-full text-xs sm:w-auto"
                disabled={searchingCandidates || !manualQuery.trim()}
                onClick={() => {
                  void handleSearchManualCandidates();
                }}
              >
                {searchingCandidates ? (
                  <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />
                ) : (
                  <Search className="mr-1 h-3.5 w-3.5" />
                )}
                Search
              </Button>
            </div>
          </div>

          {candidatesLoaded && (
            <div className="border-border space-y-2 border-t pt-3">
              <div className="flex items-center justify-between">
                <span className="font-semibold">Match Candidates ({candidates.length})</span>
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-6 text-[11px]"
                  onClick={() => {
                    setCandidates([]);
                    setCandidatesLoaded(false);
                  }}
                >
                  Clear Candidates
                </Button>
              </div>

              {candidates.length === 0 ? (
                <p className="text-muted-foreground py-2 italic">
                  No candidates found for that search.
                </p>
              ) : (
                <div className="space-y-2">
                  {candidates.map((c) => (
                    <div
                      key={`${c.sourceName}-${c.sourceId}`}
                      className="border-border bg-muted/30 flex flex-col justify-between gap-3 rounded-md border p-2.5 sm:flex-row sm:items-center"
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-1.5 sm:gap-2">
                          <span className="font-semibold break-words">{c.seriesName}</span>
                          <Badge variant="outline" className="text-[10px]">
                            {c.sourceName}
                          </Badge>
                          <span className="text-muted-foreground text-[11px]">
                            {Math.round(c.confidence * 100)}% match
                          </span>
                        </div>
                        <div className="text-muted-foreground text-[11px] break-words">
                          {c.authors.join(", ") || "Unknown author"}
                          {c.bookCount != null ? ` · ${c.bookCount} books` : ""}
                        </div>
                      </div>
                      <div className="flex shrink-0 items-center gap-2 self-end sm:self-center">
                        {c.sourceUrl && (
                          <a
                            href={c.sourceUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="text-primary hover:underline"
                          >
                            <ExternalLink className="h-3.5 w-3.5" />
                          </a>
                        )}
                        <Button
                          size="sm"
                          className="h-7 text-xs"
                          disabled={matchingCandidate}
                          onClick={() => {
                            void handleApplyMatch(c);
                          }}
                        >
                          {matchingCandidate ? (
                            <Loader2 className="mr-1 h-3 w-3 animate-spin" />
                          ) : (
                            <Check className="mr-1 h-3 w-3" />
                          )}
                          Apply Match
                        </Button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </CardContent>
      </Card>

      <div className="space-y-4">
        <h2 className="text-foreground text-lg font-bold">Owned Books ({ownedBooks.length})</h2>
        {ownedBooks.length === 0 ? (
          <p className="text-muted-foreground text-sm">No books owned.</p>
        ) : (
          <div className="space-y-2">
            {ownedBooks.map((b) => (
              <div
                key={b.id}
                onClick={() => {
                  void navigate({ to: "/library/book/$bookId", params: { bookId: String(b.id) } });
                }}
                className="group border-border bg-card hover:bg-muted/50 flex cursor-pointer items-center justify-between rounded-lg border p-3 transition-colors"
              >
                <div className="flex min-w-0 flex-1 items-center gap-3">
                  <BookOpen className="text-primary h-4 w-4 shrink-0" />
                  <div className="min-w-0 flex-1">
                    <span className="text-foreground font-semibold break-words">
                      {b.seriesPart ? `#${b.seriesPart} ` : ""}
                      {b.bookName}
                    </span>
                    <div className="text-muted-foreground text-xs break-words">
                      {b.authors.join(", ")}
                      {b.year ? ` (${b.year})` : ""}
                    </div>
                  </div>
                </div>
                <ChevronRight className="text-muted-foreground group-hover:text-foreground h-4 w-4 shrink-0" />
              </div>
            ))}
          </div>
        )}
      </div>

      {overview.isMatched && (
        <div className="space-y-4">
          <h2 className="text-lg font-bold text-amber-600 dark:text-amber-400">
            Missing Books ({missingBooks.length})
          </h2>
          {missingBooks.length === 0 ? (
            <p className="text-muted-foreground text-xs">
              No missing books detected in this series.
            </p>
          ) : (
            <div className="space-y-2">
              {missingBooks.map((mb) => (
                <div
                  key={mb.id}
                  className="flex flex-col justify-between gap-2 rounded-lg border border-amber-500/20 bg-amber-500/5 p-3 text-xs sm:flex-row sm:items-center"
                >
                  <div className="min-w-0 flex-1">
                    <span className="text-foreground font-semibold break-words">
                      {mb.position ? `Part ${mb.position} — ` : ""}
                      {mb.title}
                    </span>
                    {mb.year && <span className="text-muted-foreground"> ({mb.year})</span>}
                  </div>
                  <div className="flex shrink-0 items-center gap-2 self-end sm:self-center">
                    {mb.sourceUrl && (
                      <a
                        href={mb.sourceUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="text-primary flex items-center hover:underline"
                      >
                        <ExternalLink className="mr-1 h-3 w-3" />
                        Source
                      </a>
                    )}
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-6 text-[11px]"
                      disabled={ignoringBookId === mb.id}
                      onClick={() => {
                        void handleSetIgnored(mb, true);
                      }}
                    >
                      {ignoringBookId === mb.id ? (
                        <Loader2 className="mr-1 h-3 w-3 animate-spin" />
                      ) : (
                        <EyeOff className="mr-1 h-3 w-3" />
                      )}
                      Ignore
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {overview.isMatched && ignoredBooks.length > 0 && (
        <div className="space-y-4">
          <h2 className="text-muted-foreground text-lg font-bold">
            Ignored Books ({ignoredBooks.length})
          </h2>
          <div className="space-y-2">
            {ignoredBooks.map((ib) => (
              <div
                key={ib.id}
                className="border-border bg-card flex flex-col justify-between gap-2 rounded-lg border p-3 text-xs opacity-75 sm:flex-row sm:items-center"
              >
                <div className="min-w-0 flex-1">
                  <span className="text-muted-foreground break-words">
                    {ib.position ? `Part ${ib.position} — ` : ""}
                    {ib.title}
                  </span>
                  {ib.year && <span className="text-muted-foreground"> ({ib.year})</span>}
                </div>
                <Button
                  variant="outline"
                  size="sm"
                  className="h-6 self-end text-[11px] sm:self-center"
                  disabled={ignoringBookId === ib.id}
                  onClick={() => {
                    void handleSetIgnored(ib, false);
                  }}
                >
                  {ignoringBookId === ib.id ? (
                    <Loader2 className="mr-1 h-3 w-3 animate-spin" />
                  ) : (
                    <Eye className="mr-1 h-3 w-3" />
                  )}
                  Unignore
                </Button>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

export default SeriesDetail;
