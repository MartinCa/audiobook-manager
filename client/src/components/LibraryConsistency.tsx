import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft,
  ShieldAlert,
  Play,
  CheckCircle2,
  AlertTriangle,
  FolderX,
  Loader2,
  Info,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { OperationProgressBar } from "./OperationProgressBar";
import { DiffDisplay } from "./DiffDisplay";
import { DeleteFileDialog } from "./DeleteFileDialog";
import { BulkDeleteDirectoriesDialog } from "./BulkDeleteDirectoriesDialog";
import { TagMismatchResolveDialog } from "./TagMismatchResolveDialog";
import { consistencyApi } from "@/services/api";
import { useSignalREvent } from "@/hooks/useSignalR";
import { useOperationResync } from "@/hooks/useOperationResync";
import { handleApiError } from "@/lib/api";
import {
  getIssueTypeLabel,
  getBulkResolveDescription,
  notifyConsistencyResolveResult,
  notifyOrphanResolveResult,
} from "@/helpers/consistencyHelpers";
import { toast } from "sonner";
import type { ConsistencyIssue } from "@/types/ConsistencyIssue";
import type { OrphanDirectory } from "@/types/OrphanDirectory";

interface ProgressPayload {
  message: string;
  booksChecked: number;
  totalBooks: number;
  issuesFound: number;
}

interface CompletePayload {
  totalBooksChecked: number;
  totalIssuesFound: number;
}

const CONSISTENCY_CHECK_OPERATION_KEY = "consistency-check";

type PendingResolve =
  | { kind: "single"; issue: ConsistencyIssue }
  | { kind: "selected"; issueType: string; issueIds: number[] }
  | { kind: "byType"; issueType: string; count: number };

export function LibraryConsistency() {
  const queryClient = useQueryClient();

  // Check state
  const [checking, setChecking] = useState(false);
  const [checkProgress, setCheckProgress] = useState<ProgressPayload | null>(null);
  const [checkCompleteResult, setCheckCompleteResult] = useState<CompletePayload | null>(null);

  // Selection state
  const [selectedIssueIds, setSelectedIssueIds] = useState<Set<number>>(new Set());
  const [resolvingIds, setResolvingIds] = useState<Set<number>>(new Set());
  const [resolvingTypes, setResolvingTypes] = useState<Set<string>>(new Set());
  const [resolvingSelected, setResolvingSelected] = useState(false);

  // Orphan dialog state
  const [orphanToDelete, setOrphanToDelete] = useState<OrphanDirectory | null>(null);
  const [deleteAllOrphansOpen, setDeleteAllOrphansOpen] = useState(false);

  // Resolve confirmation state
  const [pendingResolve, setPendingResolve] = useState<PendingResolve | null>(null);
  const [confirmingResolve, setConfirmingResolve] = useState(false);

  // Tag mismatch selective-resolution state
  const [tagMismatchIssue, setTagMismatchIssue] = useState<ConsistencyIssue | null>(null);

  const { data, isLoading: loading } = useQuery({
    queryKey: ["consistency"],
    queryFn: async () => {
      const [issuesData, orphansData] = await Promise.all([
        consistencyApi.getIssues(),
        consistencyApi.getOrphanDirectories().catch(() => []),
      ]);
      return { issues: issuesData, orphanDirs: orphansData };
    },
  });

  const issues = data?.issues ?? [];
  const orphanDirs = data?.orphanDirs ?? [];

  useSignalREvent<ProgressPayload>("ConsistencyCheckProgress", (data) => {
    setChecking(true);
    setCheckProgress(data);
  });

  useSignalREvent<CompletePayload>("ConsistencyCheckComplete", (data) => {
    setChecking(false);
    setCheckProgress(null);
    setCheckCompleteResult(data);
    toast.success(
      `Check complete: ${data.totalBooksChecked} books checked, ${data.totalIssuesFound} issues found`,
    );
    void queryClient.invalidateQueries({ queryKey: ["consistency"] });
  });

  // Recover from a missed check (started elsewhere, or events missed while disconnected) on
  // mount and after a SignalR reconnect, rather than looking idle while one is still running.
  useOperationResync(CONSISTENCY_CHECK_OPERATION_KEY, (status) => {
    if (status.isRunning) {
      setChecking(true);
      setCheckProgress(
        (prev) =>
          prev ?? {
            message: "Resuming check...",
            booksChecked: status.processed,
            totalBooks: status.total,
            issuesFound: 0,
          },
      );
    } else {
      setChecking(false);
      setCheckProgress(null);
    }
  });

  const handleStartCheck = async () => {
    setChecking(true);
    setCheckCompleteResult(null);
    try {
      await consistencyApi.startCheck();
      toast.success("Consistency check started in background");
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setChecking(false);
    }
  };

  const handleResolveSingle = async (issue: ConsistencyIssue) => {
    setResolvingIds((prev) => new Set(prev).add(issue.id));
    try {
      const result = await consistencyApi.resolveIssue(issue.id);
      notifyConsistencyResolveResult(result);
      void queryClient.invalidateQueries({ queryKey: ["consistency"] });
      setSelectedIssueIds((prev) => {
        const next = new Set(prev);
        next.delete(issue.id);
        return next;
      });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setResolvingIds((prev) => {
        const next = new Set(prev);
        next.delete(issue.id);
        return next;
      });
    }
  };

  const handleResolveSelected = async (issueIds: number[]) => {
    if (issueIds.length === 0) return;
    setResolvingSelected(true);
    try {
      const res = await consistencyApi.resolveSelected(issueIds);
      toast.success(`Resolved ${res.resolved} issues (${res.failed} failed)`);
      void queryClient.invalidateQueries({ queryKey: ["consistency"] });
      setSelectedIssueIds(new Set());
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setResolvingSelected(false);
    }
  };

  const handleResolveByType = async (issueType: string) => {
    setResolvingTypes((prev) => new Set(prev).add(issueType));
    try {
      const res = await consistencyApi.resolveByType(issueType);
      toast.success(`Resolved ${res.resolved} issues of type "${issueType}"`);
      void queryClient.invalidateQueries({ queryKey: ["consistency"] });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setResolvingTypes((prev) => {
        const next = new Set(prev);
        next.delete(issueType);
        return next;
      });
    }
  };

  const onResolveClick = (issue: ConsistencyIssue) => {
    if (issue.issueType === "MissingMediaFile") {
      setPendingResolve({ kind: "single", issue });
    } else if (issue.issueType === "TagMismatch") {
      setTagMismatchIssue(issue);
    } else {
      void handleResolveSingle(issue);
    }
  };

  const handleResolveTagMismatch = async (
    issueId: number,
    fieldValues: Record<string, string | null>,
  ) => {
    setResolvingIds((prev) => new Set(prev).add(issueId));
    try {
      const result = await consistencyApi.resolveTagMismatch(issueId, fieldValues);
      notifyConsistencyResolveResult(result);
      void queryClient.invalidateQueries({ queryKey: ["consistency"] });
      setSelectedIssueIds((prev) => {
        const next = new Set(prev);
        next.delete(issueId);
        return next;
      });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setResolvingIds((prev) => {
        const next = new Set(prev);
        next.delete(issueId);
        return next;
      });
    }
  };

  const onResolveSelectedClick = (issueType: string, issueIds: number[]) => {
    if (issueIds.length === 0) return;
    setPendingResolve({ kind: "selected", issueType, issueIds });
  };

  const onResolveByTypeClick = (issueType: string, count: number) => {
    setPendingResolve({ kind: "byType", issueType, count });
  };

  const confirmPendingResolve = async () => {
    if (!pendingResolve) return;
    setConfirmingResolve(true);
    try {
      if (pendingResolve.kind === "single") {
        await handleResolveSingle(pendingResolve.issue);
      } else if (pendingResolve.kind === "selected") {
        await handleResolveSelected(pendingResolve.issueIds);
      } else {
        await handleResolveByType(pendingResolve.issueType);
      }
      setPendingResolve(null);
    } finally {
      setConfirmingResolve(false);
    }
  };

  const handleDeleteOrphan = async () => {
    if (!orphanToDelete) return;
    try {
      const res = await consistencyApi.resolveOrphanDirectory(orphanToDelete.id);
      notifyOrphanResolveResult(res);
      void queryClient.invalidateQueries({ queryKey: ["consistency"] });
      setOrphanToDelete(null);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    }
  };

  const handleDeleteAllOrphans = async () => {
    try {
      const res = await consistencyApi.resolveAllOrphanDirectories();
      if (res.retained > 0) {
        toast.success(
          `Deleted ${res.resolved} orphaned directories (${res.retained} retained with audio files, ${res.failed} failed)`,
        );
      } else {
        toast.success(`Deleted ${res.resolved} orphaned directories (${res.failed} failed)`);
      }
      void queryClient.invalidateQueries({ queryKey: ["consistency"] });
      setDeleteAllOrphansOpen(false);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    }
  };

  // Group issues by issueType
  const groupedIssues = issues.reduce<Record<string, ConsistencyIssue[]>>((acc, issue) => {
    const list = acc[issue.issueType] || [];
    list.push(issue);
    acc[issue.issueType] = list;
    return acc;
  }, {});

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <Button variant="ghost" size="sm" render={<Link to="/library" />}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </Button>

        <Button
          variant="default"
          onClick={() => {
            void handleStartCheck();
          }}
          disabled={checking}
        >
          <Play className={`mr-2 h-4 w-4 ${checking ? "animate-spin" : ""}`} />
          {checking ? "Running Check..." : "Run Consistency Check"}
        </Button>
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <ShieldAlert className="text-primary h-6 w-6" />
          Library Consistency
        </h1>
        <p className="text-muted-foreground text-sm">
          Verifies that every book in the library has the correct file path, sidecar metadata files
          (desc.txt, reader.txt), and a cover image, and detects leftover orphaned folders.
        </p>
      </div>

      {checking && checkProgress && (
        <OperationProgressBar
          processed={checkProgress.booksChecked}
          total={checkProgress.totalBooks}
          label={`${checkProgress.message || "Checking consistency..."} (${checkProgress.issuesFound} issues found)`}
        />
      )}

      {checkCompleteResult && (
        <div className="border-primary/20 bg-primary/10 text-foreground rounded-lg border p-3 text-xs">
          Check complete: {checkCompleteResult.totalBooksChecked} books checked,{" "}
          {checkCompleteResult.totalIssuesFound} issues found.
        </div>
      )}

      <div className="space-y-6">
        <div>
          <h2 className="text-foreground text-lg font-bold">Issues ({issues.length})</h2>

          {loading ? (
            <div className="text-muted-foreground flex items-center justify-center py-12">
              <Loader2 className="text-primary mr-2 h-6 w-6 animate-spin" />
              <span className="text-sm">Checking issues...</span>
            </div>
          ) : issues.length === 0 ? (
            <Card className="mt-3 p-8 text-center">
              <CheckCircle2 className="mx-auto mb-2 h-10 w-10 text-emerald-500" />
              <h3 className="text-foreground text-base font-semibold">
                No Consistency Issues Found
              </h3>
              <p className="text-muted-foreground mt-1 text-xs">
                All files, tags, and sidecar assets match their expected state.
              </p>
            </Card>
          ) : (
            <Accordion type="multiple" className="mt-3 space-y-3">
              {Object.entries(groupedIssues).map(([type, typeIssues]) => {
                const isResolvingType = resolvingTypes.has(type);
                const selectedInGroup = typeIssues.filter((i) => selectedIssueIds.has(i.id));

                return (
                  <AccordionItem
                    key={type}
                    value={type}
                    className="border-border bg-card rounded-lg border px-4 shadow-sm"
                  >
                    <AccordionTrigger className="py-3 hover:no-underline">
                      <div className="flex min-w-0 flex-1 items-center justify-between gap-2 pr-2 text-left">
                        <div className="flex items-center gap-2">
                          <AlertTriangle className="h-4 w-4 text-amber-500" />
                          <span className="text-foreground font-semibold">
                            {getIssueTypeLabel(type)}
                          </span>
                          <Tooltip>
                            <TooltipTrigger
                              render={
                                <Info
                                  className="text-muted-foreground hidden h-3.5 w-3.5 sm:inline-block"
                                  onClick={(e) => e.stopPropagation()}
                                />
                              }
                            />
                            <TooltipContent className="max-w-xs">
                              {getBulkResolveDescription(type)}
                            </TooltipContent>
                          </Tooltip>
                        </div>
                        <Badge
                          variant="secondary"
                          className="bg-amber-500/15 text-amber-600 dark:text-amber-400"
                        >
                          {typeIssues.length}
                        </Badge>
                      </div>
                    </AccordionTrigger>

                    <AccordionContent className="border-border border-t pt-4 pb-4">
                      <div className="space-y-4">
                        <div className="border-border/60 bg-muted/40 text-muted-foreground flex items-start gap-2.5 rounded-md border p-3 text-xs leading-relaxed">
                          <Info className="text-primary mt-0.5 h-4 w-4 shrink-0" />
                          <span>{getBulkResolveDescription(type)}</span>
                        </div>

                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <div className="flex items-center gap-2">
                            <input
                              type="checkbox"
                              checked={
                                selectedInGroup.length === typeIssues.length &&
                                typeIssues.length > 0
                              }
                              onChange={(e) => {
                                const check = e.target.checked;
                                setSelectedIssueIds((prev) => {
                                  const next = new Set(prev);
                                  for (const i of typeIssues) {
                                    if (check) next.add(i.id);
                                    else next.delete(i.id);
                                  }
                                  return next;
                                });
                              }}
                              className="border-border h-4 w-4 rounded"
                            />
                            <span className="text-muted-foreground text-xs">
                              Select all visible ({selectedInGroup.length} selected)
                            </span>
                          </div>

                          <div className="flex items-center gap-2">
                            {selectedInGroup.length > 0 && (
                              <Button
                                size="sm"
                                variant="outline"
                                disabled={resolvingSelected}
                                onClick={() => {
                                  onResolveSelectedClick(
                                    type,
                                    selectedInGroup.map((i) => i.id),
                                  );
                                }}
                              >
                                Resolve Selected ({selectedInGroup.length})
                              </Button>
                            )}

                            <Button
                              size="sm"
                              variant="secondary"
                              disabled={isResolvingType}
                              onClick={() => {
                                onResolveByTypeClick(type, typeIssues.length);
                              }}
                            >
                              {isResolvingType ? (
                                <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
                              ) : null}
                              Resolve All {typeIssues.length}
                            </Button>
                          </div>
                        </div>

                        <div className="space-y-2">
                          {typeIssues.map((issue) => {
                            const isResolving = resolvingIds.has(issue.id);
                            const isChecked = selectedIssueIds.has(issue.id);

                            return (
                              <div
                                key={issue.id}
                                className="border-border bg-muted/30 flex flex-col gap-2 rounded-md border p-3 sm:flex-row sm:items-center sm:justify-between"
                              >
                                <div className="flex min-w-0 flex-1 items-start gap-3">
                                  <input
                                    type="checkbox"
                                    checked={isChecked}
                                    onChange={(e) => {
                                      const checked = e.target.checked;
                                      setSelectedIssueIds((prev) => {
                                        const next = new Set(prev);
                                        if (checked) next.add(issue.id);
                                        else next.delete(issue.id);
                                        return next;
                                      });
                                    }}
                                    className="border-border mt-1 h-4 w-4 shrink-0 rounded"
                                  />

                                  <div className="min-w-0 flex-1 space-y-1">
                                    <Link
                                      to="/library/book/$bookId"
                                      params={{ bookId: String(issue.audiobookId) }}
                                      className="text-primary text-xs font-semibold break-words hover:underline"
                                    >
                                      {issue.authors.join(", ")} &mdash; {issue.bookName}
                                    </Link>
                                    <p className="text-muted-foreground text-xs break-words">
                                      {issue.description}
                                    </p>

                                    {issue.expectedValue && issue.actualValue ? (
                                      <DiffDisplay
                                        expected={issue.expectedValue}
                                        actual={issue.actualValue}
                                      />
                                    ) : issue.expectedValue ? (
                                      <div className="text-muted-foreground text-[11px] break-all">
                                        Expected: {issue.expectedValue}
                                      </div>
                                    ) : null}
                                  </div>
                                </div>

                                <Button
                                  size="sm"
                                  variant="outline"
                                  disabled={isResolving}
                                  onClick={() => {
                                    onResolveClick(issue);
                                  }}
                                  className="w-full shrink-0 self-stretch sm:w-auto sm:self-center"
                                >
                                  {isResolving ? (
                                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                                  ) : (
                                    "Resolve"
                                  )}
                                </Button>
                              </div>
                            );
                          })}
                        </div>
                      </div>
                    </AccordionContent>
                  </AccordionItem>
                );
              })}
            </Accordion>
          )}
        </div>

        {orphanDirs.length > 0 && (
          <div>
            <Accordion type="multiple" className="mt-3 space-y-3">
              <AccordionItem
                value="orphans"
                className="border-border bg-card rounded-lg border px-4 shadow-sm"
              >
                <AccordionTrigger className="py-3 hover:no-underline">
                  <div className="flex min-w-0 flex-1 items-center justify-between gap-2 pr-2 text-left">
                    <div className="flex items-center gap-2">
                      <FolderX className="h-4 w-4 text-amber-500" />
                      <span className="text-foreground font-semibold">Orphaned Directories</span>
                    </div>
                    <Badge
                      variant="secondary"
                      className="bg-amber-500/15 text-amber-600 dark:text-amber-400"
                    >
                      {orphanDirs.length}
                    </Badge>
                  </div>
                </AccordionTrigger>

                <AccordionContent className="border-border border-t pt-4 pb-4">
                  <div className="space-y-4">
                    <div className="border-border/60 bg-muted/40 text-muted-foreground flex items-start gap-2.5 rounded-md border p-3 text-xs leading-relaxed">
                      <Info className="text-primary mt-0.5 h-4 w-4 shrink-0" />
                      <span>
                        Empty or leftover folders in the library that no longer contain an
                        audiobook. Deleting removes the folder and any leftover files in it.
                      </span>
                    </div>

                    <div className="flex flex-wrap items-center justify-end gap-2">
                      <Button
                        variant="destructive"
                        size="sm"
                        onClick={() => setDeleteAllOrphansOpen(true)}
                      >
                        Delete All {orphanDirs.length}
                      </Button>
                    </div>

                    <div className="space-y-2">
                      {orphanDirs.map((dir) => (
                        <div
                          key={dir.id}
                          className="border-border bg-muted/30 flex flex-col justify-between gap-2 rounded-md border p-3 sm:flex-row sm:items-center"
                        >
                          <span className="text-muted-foreground min-w-0 flex-1 font-mono text-xs break-all">
                            {dir.directoryPath}
                          </span>
                          <Button
                            variant="outline"
                            size="sm"
                            className="w-full shrink-0 sm:w-auto"
                            onClick={() => setOrphanToDelete(dir)}
                          >
                            Delete
                          </Button>
                        </div>
                      ))}
                    </div>
                  </div>
                </AccordionContent>
              </AccordionItem>
            </Accordion>
          </div>
        )}
      </div>

      <Dialog
        open={Boolean(pendingResolve)}
        onOpenChange={(open) => {
          if (!open) setPendingResolve(null);
        }}
      >
        <DialogContent className="w-[calc(100vw-2rem)] p-4 sm:max-w-md sm:p-6">
          <DialogHeader>
            <DialogTitle>Confirm Resolution</DialogTitle>
          </DialogHeader>
          <div className="space-y-4 py-2">
            <p className="text-muted-foreground text-xs">
              {pendingResolve?.kind === "single" && (
                <>
                  This will remove <strong>1 audiobook</strong> with missing media files from the
                  database and clean up empty directories (or keep the book and refresh its status
                  if the file has been restored to disk). This action cannot be undone.
                </>
              )}
              {pendingResolve?.kind === "selected" &&
                (pendingResolve.issueType === "MissingMediaFile" ? (
                  <>
                    This will remove <strong>the selected {pendingResolve.issueIds.length}</strong>{" "}
                    audiobooks with missing media files from the database and clean up empty
                    directories (or keep books and refresh their status if files have been restored
                    to disk). This action cannot be undone.
                  </>
                ) : (
                  <>
                    This will resolve the selected <strong>{pendingResolve.issueIds.length}</strong>{" "}
                    {getIssueTypeLabel(pendingResolve.issueType)} issue
                    {pendingResolve.issueIds.length === 1 ? "" : "s"}.{" "}
                    {getBulkResolveDescription(pendingResolve.issueType)}
                  </>
                ))}
              {pendingResolve?.kind === "byType" &&
                (pendingResolve.issueType === "MissingMediaFile" ? (
                  <>
                    This will remove <strong>all {pendingResolve.count}</strong> audiobooks with
                    missing media files from the database and clean up empty directories (or keep
                    books and refresh their status if files have been restored to disk). This action
                    cannot be undone.
                  </>
                ) : (
                  <>
                    This will resolve all <strong>{pendingResolve.count}</strong>{" "}
                    {getIssueTypeLabel(pendingResolve.issueType)} issues.{" "}
                    {getBulkResolveDescription(pendingResolve.issueType)}
                  </>
                ))}
            </p>
            <div className="border-border flex flex-col-reverse justify-end gap-2 border-t pt-4 sm:flex-row">
              <Button
                variant="outline"
                className="w-full sm:w-auto"
                onClick={() => setPendingResolve(null)}
              >
                Cancel
              </Button>
              <Button
                variant="destructive"
                className="w-full sm:w-auto"
                disabled={confirmingResolve}
                onClick={() => {
                  void confirmPendingResolve();
                }}
              >
                {confirmingResolve
                  ? "Resolving..."
                  : pendingResolve?.kind === "selected"
                    ? "Resolve Selected"
                    : pendingResolve?.kind === "byType"
                      ? "Resolve All"
                      : "Remove"}
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>

      {orphanToDelete && (
        <DeleteFileDialog
          open={Boolean(orphanToDelete)}
          onOpenChange={(open) => {
            if (!open) setOrphanToDelete(null);
          }}
          targetPath={orphanToDelete.directoryPath}
          title="Delete Orphaned Directory"
          description="Are you sure you want to permanently delete this empty or orphaned folder? (Directories containing audio files will be preserved)."
          confirmButtonText="Delete Permanently"
          onConfirmDelete={handleDeleteOrphan}
        />
      )}

      <BulkDeleteDirectoriesDialog
        open={deleteAllOrphansOpen}
        onOpenChange={setDeleteAllOrphansOpen}
        directories={orphanDirs.map((d) => ({ id: d.id, directoryPath: d.directoryPath }))}
        title="Delete All Orphaned Directories"
        description={
          <>
            This will permanently delete <strong>all {orphanDirs.length}</strong> orphaned
            directories and any leftover files in them (directories containing audio files will be
            preserved).
          </>
        }
        confirmButtonText="Delete All"
        onConfirmDelete={handleDeleteAllOrphans}
      />

      <TagMismatchResolveDialog
        open={Boolean(tagMismatchIssue)}
        onOpenChange={(open) => {
          if (!open) setTagMismatchIssue(null);
        }}
        issue={tagMismatchIssue}
        onResolve={handleResolveTagMismatch}
      />
    </div>
  );
}

export default LibraryConsistency;
