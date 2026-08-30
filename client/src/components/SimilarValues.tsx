import { useState } from "react";
import { Link } from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Layers, RefreshCw, ArrowRight, Loader2, Users, BookMarked } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Badge } from "@/components/ui/badge";
import { AlignTargetDialog } from "./AlignTargetDialog";
import { OperationProgressBar } from "./OperationProgressBar";
import { similarValuesApi } from "@/services/api";
import { useSignalREvent } from "@/hooks/useSignalR";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
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

export function SimilarValues() {
  const queryClient = useQueryClient();
  const [activeTab, setActiveTab] = useState<"author" | "series">("author");
  const [selectedGroup, setSelectedGroup] = useState<SimilarValueGroup | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);

  // Operation progress
  const [aligning, setAligning] = useState(false);
  const [alignProgress, setAlignProgress] = useState<ProgressPayload | null>(null);

  const {
    data: groups = [],
    isLoading: loading,
    refetch,
  } = useQuery({
    queryKey: ["similarValues", activeTab],
    queryFn: () =>
      activeTab === "author"
        ? similarValuesApi.getSimilarAuthors()
        : similarValuesApi.getSimilarSeries(),
  });

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
    void queryClient.invalidateQueries({ queryKey: ["similarValues"] });
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
        <Button variant="ghost" size="sm" asChild>
          <Link to="/library">
            <ArrowLeft className="mr-2 h-4 w-4" />
            Back to Library
          </Link>
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

      <Tabs value={activeTab} onValueChange={(val) => setActiveTab(val as "author" | "series")}>
        <TabsList className="mb-4">
          <TabsTrigger value="author" className="flex items-center gap-2">
            <Users className="h-4 w-4" />
            Similar Authors
          </TabsTrigger>
          <TabsTrigger value="series" className="flex items-center gap-2">
            <BookMarked className="h-4 w-4" />
            Similar Series
          </TabsTrigger>
        </TabsList>
      </Tabs>

      {loading ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Detecting near duplicates...</p>
        </div>
      ) : groups.length === 0 ? (
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
          {groups.map((group, index) => (
            <Card key={index} className="p-4">
              <CardContent className="p-0">
                <div className="border-border flex flex-wrap items-center justify-between gap-4 border-b pb-3">
                  <div className="flex items-center gap-2">
                    <span className="text-foreground text-sm font-semibold">
                      Group #{index + 1}
                    </span>
                    <Badge variant="outline">{group.candidates.length} variants</Badge>
                  </div>
                  <Button size="sm" onClick={() => handleOpenDialog(group)}>
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
                      <div className="text-foreground font-semibold">{cand.value}</div>
                      <div className="text-muted-foreground mt-1">
                        {cand.books.length} {cand.books.length === 1 ? "book" : "books"}:
                      </div>
                      <ul className="text-muted-foreground mt-1 max-h-24 list-disc space-y-0.5 overflow-y-auto pl-4 text-[11px]">
                        {cand.books.map((b) => (
                          <li key={b.id}>
                            <Link to={`/library/book/${b.id}`} className="hover:underline">
                              {b.bookName}
                            </Link>
                          </li>
                        ))}
                      </ul>
                    </div>
                  ))}
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {selectedGroup && (
        <AlignTargetDialog
          open={dialogOpen}
          onOpenChange={setDialogOpen}
          candidates={selectedGroup.candidates.map((c) => c.value)}
          onConfirm={(target) => {
            void handleAlignConfirm(target);
          }}
        />
      )}
    </div>
  );
}

export default SimilarValues;
