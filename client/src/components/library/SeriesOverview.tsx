import { useState } from "react";
import { useNavigate } from "@tanstack/react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  BookMarked,
  Search,
  RefreshCw,
  CheckCircle2,
  AlertCircle,
  Loader2,
  ChevronRight,
  Layers,
  Sparkles,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { LibraryViewTabs } from "./LibraryViewTabs";
import { OperationProgressBar } from "@/components/OperationProgressBar";
import { SeriesMatchDialog } from "./SeriesMatchDialog";
import { seriesApi } from "@/services/api";
import { useSignalREvent } from "@/hooks/useSignalR";
import { foldAccents } from "@/helpers/similarValueMatcher";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";

interface SeriesRefreshProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface SeriesRefreshCompletePayload {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
  stopReason?: string;
}

export function SeriesOverviewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [filter, setFilter] = useState("");

  const [refreshing, setRefreshing] = useState(false);
  const [refreshProgress, setRefreshProgress] = useState<SeriesRefreshProgressPayload | null>(null);
  const [matchDialogOpen, setMatchDialogOpen] = useState(false);

  const {
    data: seriesList = [],
    isLoading: loading,
    refetch,
  } = useQuery({
    queryKey: ["series"],
    queryFn: () => seriesApi.getAllSeries(),
  });

  useSignalREvent<SeriesRefreshProgressPayload>("SeriesRefreshProgress", (data) => {
    setRefreshing(true);
    setRefreshProgress(data);
  });

  useSignalREvent<SeriesRefreshCompletePayload>("SeriesRefreshComplete", (arg) => {
    setRefreshing(false);
    setRefreshProgress(null);
    const msg = arg.stopReason
      ? `Refresh stopped after ${arg.totalProcessed} series: ${arg.stopReason}`
      : `Refresh complete: ${arg.totalSucceeded} of ${arg.totalProcessed} series updated${
          arg.totalFailed > 0 ? ` (${arg.totalFailed} failed)` : ""
        }`;
    toast.success(msg);
    void queryClient.invalidateQueries({ queryKey: ["series"] });
  });

  const handleRefreshAll = async () => {
    setRefreshing(true);
    try {
      await seriesApi.startRefreshAll();
      toast.success("Refreshing all series in background");
    } catch (err: unknown) {
      setRefreshing(false);
      toast.error(handleApiError(err).message);
    }
  };

  const unmatchedSeries = seriesList.filter((s) => !s.isMatched);
  const matchedCount = seriesList.filter((s) => s.isMatched).length;

  const filteredSeries = seriesList.filter((s) => {
    if (!filter.trim()) return true;
    const q = foldAccents(filter.trim().toLowerCase());
    const nameMatch = foldAccents(s.name.toLowerCase()).includes(q);
    const authorMatch = (s.authors ?? []).some((a) => foldAccents(a.toLowerCase()).includes(q));
    return nameMatch || authorMatch;
  });

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <LibraryViewTabs activeTab="series" />

        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              void refetch();
            }}
            disabled={loading || refreshing}
          >
            <RefreshCw className={`mr-2 h-4 w-4 ${loading ? "animate-spin" : ""}`} />
            Reload
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => setMatchDialogOpen(true)}
            disabled={loading || refreshing || unmatchedSeries.length === 0}
          >
            <Sparkles className="mr-2 h-4 w-4" />
            Bulk Match ({unmatchedSeries.length})
          </Button>

          <Button
            variant="default"
            size="sm"
            onClick={() => {
              void handleRefreshAll();
            }}
            disabled={loading || refreshing || matchedCount === 0}
          >
            <Layers className="mr-2 h-4 w-4" />
            {refreshing ? "Refreshing All..." : "Refresh All Series"}
          </Button>
        </div>
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <BookMarked className="text-primary h-6 w-6" />
          Series ({seriesList.length})
        </h1>
        <p className="text-muted-foreground text-sm">
          Every series in your library. Match series to metadata providers to identify missing parts
          and maintain reading orders.
        </p>
      </div>

      {refreshing && refreshProgress && (
        <OperationProgressBar
          processed={refreshProgress.processed}
          total={refreshProgress.total}
          label={`Refreshing series metadata (${refreshProgress.succeeded} succeeded, ${refreshProgress.failed} failed)`}
        />
      )}

      <div className="relative max-w-md">
        <Search className="text-muted-foreground absolute top-2.5 left-3 h-4 w-4" />
        <Input
          placeholder="Filter series or authors..."
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          className="pl-9"
        />
      </div>

      {loading && seriesList.length === 0 ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Loading series...</p>
        </div>
      ) : filteredSeries.length === 0 ? (
        <Card className="p-12 text-center">
          <BookMarked className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
          <h3 className="text-foreground text-lg font-medium">No series found</h3>
          <p className="text-muted-foreground mt-1 text-sm">
            {filter
              ? "No series match your search filter."
              : "No audiobooks with series tags have been organized yet."}
          </p>
        </Card>
      ) : (
        <div className="space-y-2">
          {filteredSeries.map((s) => (
            <div
              key={s.name}
              onClick={() => {
                void navigate({
                  to: "/library/series/$seriesName",
                  params: { seriesName: s.name },
                });
              }}
              className="group border-border bg-card hover:bg-muted/50 flex cursor-pointer items-center justify-between rounded-lg border p-3 transition-colors"
            >
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <span className="text-foreground truncate font-semibold">{s.name}</span>
                  {s.isMatched ? (
                    <Badge
                      variant="secondary"
                      className="gap-1 bg-emerald-500/15 text-[11px] text-emerald-600 dark:text-emerald-400"
                    >
                      <CheckCircle2 className="h-3 w-3" />
                      {s.matchedSourceName}
                      {s.matchConfidence != null && (
                        <span className="ml-0.5 opacity-75">
                          ({Math.round(s.matchConfidence * 100)}%)
                        </span>
                      )}
                    </Badge>
                  ) : (
                    <Badge variant="outline" className="text-muted-foreground text-[11px]">
                      Unmatched
                    </Badge>
                  )}
                </div>

                <div className="text-muted-foreground flex flex-wrap items-center gap-x-2 text-xs">
                  {s.authors && s.authors.length > 0 && (
                    <span>By {s.authors.join(", ")} &middot;</span>
                  )}
                  <span>
                    {s.ownedBookCount} {s.ownedBookCount === 1 ? "book" : "books"} owned
                  </span>
                  {s.isMatched && s.missingBookCount > 0 && (
                    <span className="font-medium text-amber-600 dark:text-amber-400">
                      &middot; {s.missingBookCount} missing
                    </span>
                  )}
                </div>
              </div>

              <div className="flex shrink-0 items-center gap-3">
                {s.missingBookCount > 0 && (
                  <Badge variant="destructive" className="gap-1 text-xs">
                    <AlertCircle className="h-3 w-3" />
                    {s.missingBookCount} missing
                  </Badge>
                )}
                <ChevronRight className="text-muted-foreground group-hover:text-foreground h-4 w-4" />
              </div>
            </div>
          ))}
        </div>
      )}

      <SeriesMatchDialog
        open={matchDialogOpen}
        onOpenChange={setMatchDialogOpen}
        series={unmatchedSeries}
        onMatched={() => {
          void queryClient.invalidateQueries({ queryKey: ["series"] });
        }}
      />
    </div>
  );
}

export default SeriesOverviewPage;
