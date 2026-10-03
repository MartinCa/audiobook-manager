import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft,
  Layers,
  RefreshCw,
  ArrowRight,
  Loader2,
  Users,
  BookMarked,
  Ban,
  ListX,
} from "lucide-react";
import { ActionButton } from "@/components/action-button";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Badge } from "@/components/ui/badge";
import type { PageSizeOption } from "@/constants/paging";
import { OperationKeys, SignalREvents } from "@/constants/signalrEvents";
import { LinkButton } from "./LinkButton";
import { AlignTargetDialog } from "./AlignTargetDialog";
import { IgnoredSimilarValuesDialog } from "./IgnoredSimilarValuesDialog";
import { OperationProgressBar } from "./OperationProgressBar";
import { SectionPager } from "./library/SectionPager";
import { similarValuesApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useSignalREvent } from "@/hooks/useSignalR";
import { useOperationResync } from "@/hooks/useOperationResync";
import { useClampedPage } from "@/hooks/useClampedPage";
import { useAsyncAction } from "@/hooks/use-async-action";
import { usePageSize } from "@/hooks/usePageSize";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type { SimilarValueGroup } from "@/types/SimilarValue";

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

function IgnorePairButton({ value, onIgnore }: { value: string; onIgnore: () => Promise<void> }) {
  const { status, run } = useAsyncAction(onIgnore);

  const handleClick = async () => {
    const outcome = await run();
    if (outcome.ok) notifications.success(`"${value}" marked as not similar`);
    else notifications.error(handleApiError(outcome.error).message);
  };

  return (
    <ActionButton
      size="icon-xs"
      variant="ghost"
      icon={Ban}
      label={`Mark "${value}" as not similar to the rest of this group`}
      resultLabel={
        status === "success" ? `"${value}" marked as not similar` : "Could not mark as not similar"
      }
      status={status}
      onClick={() => void handleClick()}
    />
  );
}

export function SimilarValues() {
  const queryClient = useQueryClient();
  const [activeTab, setActiveTab] = useState<"author" | "series">("author");
  const [selectedGroup, setSelectedGroup] = useState<SimilarValueGroup | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [ignoredDialogOpen, setIgnoredDialogOpen] = useState(false);

  // The detected groups are paged server-side: the clustering still runs over the whole
  // distinct-value set per request (detection is stateless by design), but only the requested
  // page - with per-candidate book counts, not book lists - crosses the wire.
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePageSize();

  // Operation progress
  const [aligning, setAligning] = useState(false);
  const [alignProgress, setAlignProgress] = useState<ProgressPayload | null>(null);

  const {
    data: pageData,
    isLoading: loading,
    refetch,
  } = useQuery({
    queryKey: queryKeys.similarValues.page(activeTab, page, pageSize),
    placeholderData: keepPreviousData,
    queryFn: () =>
      activeTab === "author"
        ? similarValuesApi.getSimilarAuthors(page, pageSize)
        : similarValuesApi.getSimilarSeries(page, pageSize),
  });

  const groups = (pageData?.items ?? []) as SimilarValueGroup[];
  const totalCount = pageData?.totalCount ?? 0;
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));
  const currentPage = Math.min(page, pageCount - 1);

  // An alignment folds groups together, shrinking the total while the user may sit on a later
  // page; pull the raw page back into range so the next fetch lands on a valid page.
  useClampedPage(page, pageCount, setPage);

  const handlePageSizeChange = (size: PageSizeOption) => {
    setPageSize(size);
    setPage(0);
  };

  // Recover from a missed alignment (started elsewhere, or events missed while disconnected)
  // on mount and after a SignalR reconnect, rather than looking idle while one is still running.
  // The returned invalidate is called from the alignment's event handlers so a status response
  // fetched before a real event is discarded instead of clobbering the state the event set.
  const invalidateAlign = useOperationResync(OperationKeys.similarValueAlign, (status) => {
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

  useSignalREvent<ProgressPayload>(SignalREvents.SimilarValueAlignProgress, (data) => {
    invalidateAlign();
    setAligning(true);
    setAlignProgress(data);
  });

  useSignalREvent<AlignCompletePayload>(SignalREvents.SimilarValueAlignComplete, (data) => {
    invalidateAlign();
    setAligning(false);
    setAlignProgress(null);
    notifications.success(
      `Alignment complete: ${data.totalSucceeded} succeeded, ${data.totalFailed} failed`,
    );
    // Alignment can only merge groups, so the total shrank - drop back to page 0 so the refetch
    // below never asks for a page the smaller detection result no longer has.
    setPage(0);
    void queryClient.invalidateQueries({ queryKey: queryKeys.similarValues.all() });
  });

  const handleOpenDialog = (group: SimilarValueGroup) => {
    setSelectedGroup(group);
    setDialogOpen(true);
  };

  const handleAlignConfirm = async (targetValue: string, includedValues: string[]) => {
    if (!selectedGroup) return;
    setAligning(true);
    try {
      await similarValuesApi.align(activeTab, includedValues, targetValue);
      notifications.success(`Alignment started for "${targetValue}"`);
      void queryClient.invalidateQueries({ queryKey: queryKeys.similarValues.all() });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
      setAligning(false);
    } finally {
      setSelectedGroup(null);
    }
  };

  const handleIgnorePair = async (value: string, group: SimilarValueGroup) => {
    const againstValues = group.candidates.map((c) => c.value).filter((v) => v !== value);
    await similarValuesApi.ignorePair(activeTab, value, againstValues);
    void queryClient.invalidateQueries({ queryKey: queryKeys.similarValues.all() });
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <LinkButton variant="ghost" size="sm" render={<Link to="/library" />}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </LinkButton>

        <div className="flex flex-wrap items-center gap-2">
          <Button variant="outline" onClick={() => setIgnoredDialogOpen(true)}>
            <ListX className="mr-2 h-4 w-4" />
            Show ignored
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
              key={`${currentPage * pageSize + index + 1}-${group.candidates[0]?.value ?? ""}`}
              className="p-4"
            >
              <CardContent className="p-0">
                <div className="border-border flex flex-wrap items-center justify-between gap-3 border-b pb-3">
                  <div className="flex items-center gap-2">
                    <span className="text-foreground text-sm font-semibold">
                      Group #{currentPage * pageSize + index + 1}
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
                      className="border-border bg-muted/30 flex items-start justify-between gap-2 rounded-md border p-2.5 text-xs"
                    >
                      <div className="min-w-0">
                        {activeTab === "series" ? (
                          <Link
                            to="/library/series/$seriesName"
                            params={{ seriesName: cand.value }}
                            className="text-primary font-semibold break-words hover:underline"
                          >
                            {cand.value}
                          </Link>
                        ) : cand.authorId != null ? (
                          <Link
                            to="/library/authors/$authorId"
                            params={{ authorId: String(cand.authorId) }}
                            className="text-primary font-semibold break-words hover:underline"
                          >
                            {cand.value}
                          </Link>
                        ) : (
                          <span className="text-foreground font-semibold break-words">
                            {cand.value}
                          </span>
                        )}
                        <div className="text-muted-foreground mt-1">
                          {cand.bookCount} {cand.bookCount === 1 ? "book" : "books"}
                        </div>
                      </div>
                      <IgnorePairButton
                        value={cand.value}
                        onIgnore={() => handleIgnorePair(cand.value, group)}
                      />
                    </div>
                  ))}
                </div>
              </CardContent>
            </Card>
          ))}

          <SectionPager
            currentPage={currentPage}
            pageCount={pageCount}
            totalCount={totalCount}
            pageSize={pageSize}
            onPageChange={setPage}
            onPageSizeChange={handlePageSizeChange}
          />
        </div>
      )}

      {selectedGroup && (
        <AlignTargetDialog
          // AlignTargetDialog's target/checkbox selection state only initializes on mount, not on
          // prop change (closing via ESC/backdrop only flips `open`, it does not clear
          // selectedGroup) - keying by the group's identity forces a remount instead of reusing
          // stale selection state from whichever group was open before. Detected groups are
          // disjoint clusters, so the first candidate's value alone already uniquely identifies a
          // group - no need to join every value (which a "|" in a real value could collide on).
          key={selectedGroup.candidates[0]?.value ?? ""}
          open={dialogOpen}
          onOpenChange={setDialogOpen}
          candidates={selectedGroup.candidates}
          valueType={activeTab}
          onConfirm={(target, includedValues) => {
            void handleAlignConfirm(target, includedValues);
          }}
        />
      )}

      <IgnoredSimilarValuesDialog
        open={ignoredDialogOpen}
        onOpenChange={setIgnoredDialogOpen}
        valueType={activeTab}
      />
    </div>
  );
}

export default SimilarValues;
