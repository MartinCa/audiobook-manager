import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Layers, RefreshCw, ArrowRight, Loader2, Users, BookMarked } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Badge } from "@/components/ui/badge";
import { AlignTargetDialog } from "./AlignTargetDialog";
import { OperationProgressBar } from "./OperationProgressBar";
import { similarValuesApi } from "@/services/api";
import { useSignalREvent } from "@/hooks/useSignalR";
import { useOperationResync } from "@/hooks/useOperationResync";
import { useClampedPage } from "@/hooks/useClampedPage";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
import type { SimilarValueGroup } from "@/types/SimilarValue";

const SIMILAR_VALUE_ALIGN_OPERATION_KEY = "similar-value-align";

const PAGE_SIZE = 50;

interface ProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface AlignCompletePayload {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
}

export function SimilarValues() {
  const queryClient = useQueryClient();
  const [activeTab, setActiveTab] = useState<"author" | "series">("author");
  const [selectedGroup, setSelectedGroup] = useState<SimilarValueGroup | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);

  // The detected groups are paged server-side: the clustering still runs over the whole
  // distinct-value set per request (detection is stateless by design), but only the requested
  // page - with per-candidate book counts, not book lists - crosses the wire.
  const [page, setPage] = useState(0);

  // Operation progress
  const [aligning, setAligning] = useState(false);
  const [alignProgress, setAlignProgress] = useState<ProgressPayload | null>(null);

  const {
    data: pageData,
    isLoading: loading,
    refetch,
  } = useQuery({
    queryKey: ["similarValues", activeTab, page],
    placeholderData: keepPreviousData,
    queryFn: () =>
      activeTab === "author"
        ? similarValuesApi.getSimilarAuthors(page, PAGE_SIZE)
        : similarValuesApi.getSimilarSeries(page, PAGE_SIZE),
  });

  const groups = (pageData?.items ?? []) as SimilarValueGroup[];
  const totalCount = pageData?.totalCount ?? 0;
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount - 1);

  // An alignment folds groups together, shrinking the total while the user may sit on a later
  // page; pull the raw page back into range so the next fetch lands on a valid page.
  useClampedPage(page, pageCount, setPage);

  useSignalREvent<ProgressPayload>("SimilarValueAlignProgress", (data) => {
    setAligning(true);
    setAlignProgress(data);
  });

  useSignalREvent<AlignCompletePayload>("SimilarValueAlignComplete", (data) => {
    setAligning(false);
    setAlignProgress(null);
    toast.success(
      `Alignment complete: ${data.totalSucceeded} succeeded, ${data.totalFailed} failed`,
    );
    // Alignment can only merge groups, so the total shrank - drop back to page 0 so the refetch
    // below never asks for a page the smaller detection result no longer has.
    setPage(0);
    void queryClient.invalidateQueries({ queryKey: ["similarValues"] });
  });

  // Recover from a missed alignment (started elsewhere, or events missed while disconnected)
  // on mount and after a SignalR reconnect, rather than looking idle while one is still running.
  useOperationResync(SIMILAR_VALUE_ALIGN_OPERATION_KEY, (status) => {
    if (status.isRunning) {
      setAligning(true);
      setAlignProgress(
        (prev) =>
          prev ?? {
            processed: status.processed,
            total: status.total,
            succeeded: 0,
            failed: 0,
          },
      );
    } else {
      setAligning(false);
      setAlignProgress(null);
    }
  });

  const handleOpenDialog = (group: SimilarValueGroup) => {
    setSelectedGroup(group);
    setDialogOpen(true);
  };

  const handleAlignConfirm = async (targetValue: string) => {
    if (!selectedGroup) return;
    setAligning(true);
    const candidateStrings = selectedGroup.candidates.map((c) => c.value);
    try {
      await similarValuesApi.align(activeTab, candidateStrings, targetValue);
      toast.success(`Alignment started for "${targetValue}"`);
      void queryClient.invalidateQueries({ queryKey: ["similarValues"] });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setAligning(false);
    } finally {
      setSelectedGroup(null);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <Button variant="ghost" size="sm" render={<Link to="/library" />}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </Button>

        <Button
          variant="outline"
          onClick={() => {
            void refetch();
          }}
          disabled={loading}
        >
          <RefreshCw className={`mr-2 h-4 w-4 ${loading ? "animate-spin" : ""}`} />
          Refresh Detection
        </Button>
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <Layers className="text-primary h-6 w-6" />
          Similar Values Alignment
        </h1>
        <p className="text-muted-foreground text-sm">
          Detect and merge near-duplicate author names and series titles across your library.
        </p>
      </div>

      {aligning && alignProgress && (
        <OperationProgressBar
          processed={alignProgress.processed}
          total={alignProgress.total}
          label={`Aligning values (${alignProgress.succeeded} succeeded, ${alignProgress.failed} failed)`}
        />
      )}

      <Tabs
        value={activeTab}
        onValueChange={(val) => {
          setActiveTab(val as "author" | "series");
          setPage(0);
        }}
      >
        <TabsList className="mb-4 grid w-full grid-cols-2 sm:inline-flex sm:w-auto">
          <TabsTrigger value="author" className="flex items-center gap-2 text-xs">
            <Users className="h-4 w-4" />
            Similar Authors
          </TabsTrigger>
          <TabsTrigger value="series" className="flex items-center gap-2 text-xs">
            <BookMarked className="h-4 w-4" />
            Similar Series
          </TabsTrigger>
        </TabsList>
      </Tabs>

      {loading && groups.length === 0 ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Detecting near duplicates...</p>
        </div>
      ) : totalCount === 0 ? (
        <Card className="p-12 text-center">
          <Layers className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
          <h3 className="text-foreground text-lg font-medium">
            No similar {activeTab === "author" ? "authors" : "series"} found
          </h3>
          <p className="text-muted-foreground mt-1 text-sm">
            All names appear unique and consistent across your collection.
          </p>
        </Card>
      ) : (
        <div className="space-y-4">
          {/* The index bases on the whole detection result, but only the current page renders. */}
          {groups.map((group, index) => (
            <Card
              key={`${currentPage * PAGE_SIZE + index + 1}-${group.candidates[0]?.value ?? ""}`}
              className="p-4"
            >
              <CardContent className="p-0">
                <div className="border-border flex flex-wrap items-center justify-between gap-3 border-b pb-3">
                  <div className="flex items-center gap-2">
                    <span className="text-foreground text-sm font-semibold">
                      Group #{currentPage * PAGE_SIZE + index + 1}
                    </span>
                    <Badge variant="outline">{group.candidates.length} variants</Badge>
                  </div>
                  <Button
                    size="sm"
                    className="w-full sm:w-auto"
                    onClick={() => handleOpenDialog(group)}
                  >
                    Align Group
                    <ArrowRight className="ml-2 h-4 w-4" />
                  </Button>
                </div>

                <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
                  {group.candidates.map((cand) => (
                    <div
                      key={cand.value}
                      className="border-border bg-muted/30 rounded-md border p-2.5 text-xs"
                    >
                      <div className="text-foreground font-semibold break-words">{cand.value}</div>
                      <div className="text-muted-foreground mt-1">
                        {cand.bookCount} {cand.bookCount === 1 ? "book" : "books"}
                      </div>
                    </div>
                  ))}
                </div>
              </CardContent>
            </Card>
          ))}

          {pageCount > 1 && (
            <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
              <span className="text-muted-foreground text-xs">
                Showing {currentPage * PAGE_SIZE + 1}–
                {Math.min((currentPage + 1) * PAGE_SIZE, totalCount)} of {totalCount} groups
              </span>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={currentPage === 0}
                  onClick={() => setPage(currentPage - 1)}
                >
                  Previous
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={currentPage >= pageCount - 1}
                  onClick={() => setPage(currentPage + 1)}
                >
                  Next
                </Button>
              </div>
            </div>
          )}
        </div>
      )}

      {selectedGroup && (
        <AlignTargetDialog
          open={dialogOpen}
          onOpenChange={setDialogOpen}
          candidates={selectedGroup.candidates}
          valueType={activeTab}
          onConfirm={(target) => {
            void handleAlignConfirm(target);
          }}
        />
      )}
    </div>
  );
}

export default SimilarValues;
