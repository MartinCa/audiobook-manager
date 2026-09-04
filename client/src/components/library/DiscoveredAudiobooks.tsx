import { useState, useEffect } from "react";
import { Link } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft,
  Search,
  X,
  Scan,
  CheckSquare,
  Square,
  FolderInput,
  Loader2,
  FileAudio,
  CheckCircle2,
  AlertTriangle,
  Clock,
  HardDrive,
  RotateCcw,
  Trash2,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { OperationProgressBar } from "@/components/OperationProgressBar";
import { BookEditForm } from "@/components/BookEditForm";
import { DuplicateTargetDialog } from "../DuplicateTargetDialog";
import { DeleteFileDialog } from "../DeleteFileDialog";
import { AudiobookFileDetails } from "../AudiobookFileDetails";
import { libraryApi, audiobookApi, filesApi, queueApi } from "@/services/api";
import { useSignalREvent, useSignalRReconnected } from "@/hooks/useSignalR";
import { useTargetCollision } from "@/hooks/useTargetCollision";
import { handleApiError } from "@/lib/api";
import { formatDuration, formatFileSize } from "@/helpers/formatHelpers";
import { toAudiobook } from "@/helpers/audiobookMapping";
import { pathsEqual } from "@/helpers/pathHelpers";
import { toast } from "sonner";
import type { DiscoveredAudiobook } from "@/types/DiscoveredAudiobook";
import type { Audiobook } from "@/types/Audiobook";

interface ScanProgressPayload {
  message: string;
  filesScanned: number;
  totalFiles: number;
}

interface ScanCompletePayload {
  totalFilesScanned: number;
  newFilesDiscovered: number;
  alreadyTracked: number;
}

interface ImportProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface ImportCompletePayload {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
}

interface OrganizeProgressPayload {
  originalFileLocation: string;
  progress: number;
  progressMessage: string;
}

interface OrganizeQueueErrorPayload {
  originalFileLocation: string;
  error: string;
}

export function DiscoveredAudiobooks() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize] = useState(25);
  const [selectedPaths, setSelectedPaths] = useState<Set<string>>(new Set());

  // Live organize progress/error for the per-item "Import to Library" action, keyed by the
  // discovered file's path (same key the backend reports UpdateProgress/QueueError under).
  const [organizeOverrides, setOrganizeOverrides] = useState<
    Record<string, { progress?: number; message?: string; error?: string }>
  >({});

  // Scan state
  const [scanning, setScanning] = useState(false);
  const [scanProgress, setScanProgress] = useState<{
    message: string;
    scanned: number;
    total: number;
  } | null>(null);
  const [scanResult, setScanResult] = useState<ScanCompletePayload | null>(null);

  // Bulk import state
  const [importing, setImporting] = useState(false);
  const [importProgress, setImportProgress] = useState<{
    processed: number;
    total: number;
    succeeded: number;
    failed: number;
  } | null>(null);

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(search);
      setPage(1);
    }, 300);
    return () => clearTimeout(timer);
  }, [search]);

  const { data, isLoading: loading } = useQuery({
    queryKey: ["discoveredAudiobooks", debouncedSearch, page, pageSize],
    queryFn: () =>
      libraryApi.getDiscovered(
        pageSize,
        (page - 1) * pageSize,
        debouncedSearch.trim() || undefined,
      ),
  });

  const books: DiscoveredAudiobook[] = data?.items ?? [];
  const totalCount = data?.total ?? 0;

  // Organize tasks whose json_audiobook failed to deserialize - including ones dead-lettered
  // past the retry threshold - so a permanently-stuck file isn't invisible until someone digs
  // through the logs. See #1322.
  const { data: failedTasks = [] } = useQuery({
    queryKey: ["failedOrganizeTasks"],
    queryFn: () => queueApi.getFailedTasks(),
  });

  // SignalR scan events
  useSignalREvent<ScanProgressPayload>("LibraryScanProgress", (data) => {
    setScanning(true);
    setScanProgress({
      message: data.message,
      scanned: data.filesScanned,
      total: data.totalFiles,
    });
  });

  useSignalREvent<ScanCompletePayload>("LibraryScanComplete", (data) => {
    setScanning(false);
    setScanProgress(null);
    setScanResult(data);
    toast.success(
      `Scan complete: ${data.newFilesDiscovered} new files, ${data.alreadyTracked} already tracked`,
    );
    void queryClient.invalidateQueries({
      queryKey: ["discoveredAudiobooks"],
    });
  });

  // SignalR import events
  useSignalREvent<ImportProgressPayload>("DiscoveredImportProgress", (data) => {
    setImporting(true);
    setImportProgress(data);
  });

  useSignalREvent<ImportCompletePayload>("DiscoveredImportComplete", (data) => {
    setImporting(false);
    setImportProgress(null);
    setSelectedPaths(new Set());
    toast.success(`Import complete: ${data.totalSucceeded} succeeded, ${data.totalFailed} failed`);
    void queryClient.invalidateQueries({
      queryKey: ["discoveredAudiobooks"],
    });
  });

  // SignalR single-item organize events (queued via "Import to Library" below)
  useSignalREvent<OrganizeProgressPayload>("UpdateProgress", (payload) => {
    setOrganizeOverrides((prev) => {
      const next = { ...prev };
      const matchedKey =
        Object.keys(next).find((k) => pathsEqual(k, payload.originalFileLocation)) ??
        payload.originalFileLocation;

      if (payload.progress >= 100) {
        delete next[matchedKey];
      } else {
        next[matchedKey] = {
          progress: payload.progress,
          message: payload.progressMessage,
        };
      }
      return next;
    });

    if (payload.progress >= 100) {
      void queryClient.invalidateQueries({
        queryKey: ["discoveredAudiobooks"],
      });
    }
  });

  useSignalREvent<OrganizeQueueErrorPayload>("QueueError", (payload) => {
    setOrganizeOverrides((prev) => {
      const next = { ...prev };
      const matchedKey =
        Object.keys(next).find((k) => pathsEqual(k, payload.originalFileLocation)) ??
        payload.originalFileLocation;
      next[matchedKey] = {
        error: payload.error,
      };
      return next;
    });
    toast.error(`Organize failed: ${payload.error}`);

    // A row retried from the Failed Organize Tasks section below gets exactly one more attempt
    // (see QueuedOrganizeTaskRepository.RetryQueuedOrganizeTaskAsync); if the JSON is still
    // broken, the worker re-fails and dead-letters it again within its next idle-poll tick. The
    // retry handler already invalidates this query once (on the request succeeding, which just
    // means "queued for another try"), but that leaves no signal for the *outcome* - this is the
    // one that brings the row back into view once it actually fails again, instead of the user
    // being told "queued" and then hearing nothing further.
    void queryClient.invalidateQueries({ queryKey: ["failedOrganizeTasks"] });
  });

  // A dropped/re-established connection may have missed progress or completion events for
  // an in-flight import; re-fetching re-derives the list's state the same way it does on mount.
  useSignalRReconnected(() => {
    void queryClient.invalidateQueries({
      queryKey: ["discoveredAudiobooks"],
    });
  });

  const handleStartScan = async () => {
    setScanning(true);
    setScanResult(null);
    try {
      await libraryApi.startScan();
      toast.success("Library scan started in background");
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setScanning(false);
    }
  };

  const handleToggleSelectPath = (path: string) => {
    setSelectedPaths((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  };

  const wellTaggedEligible = books.filter((b) => b.isWellTagged && !b.isDuplicate);

  const handleSelectAllWellTagged = () => {
    if (selectedPaths.size === wellTaggedEligible.length) {
      setSelectedPaths(new Set());
    } else {
      setSelectedPaths(new Set(wellTaggedEligible.map((b) => b.fullPath)));
    }
  };

  const handleBulkImport = async () => {
    if (selectedPaths.size === 0) return;
    setImporting(true);
    try {
      await libraryApi.bulkImport(Array.from(selectedPaths));
      toast.success(`Import queued for ${selectedPaths.size} books`);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setImporting(false);
    }
  };

  const [deleteTargetPath, setDeleteTargetPath] = useState<string | null>(null);

  const executeDeleteDiscovered = async (path: string) => {
    try {
      await filesApi.deleteBook(path);
      await libraryApi.deleteDiscovered(path);
      toast.success("File deleted and record removed");
      void queryClient.invalidateQueries({
        queryKey: ["discoveredAudiobooks"],
      });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    }
  };

  const proceedOrganizeDiscovered = async (book: Audiobook) => {
    const path = book.fileInfo?.fullPath ?? "";
    try {
      await audiobookApi.organizeBook(book);
      toast.success("Book added to organization queue");
      if (path) {
        setOrganizeOverrides((prev) => ({
          ...prev,
          [path]: { progress: 0, message: "Queued..." },
        }));
      }
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    }
  };

  const [removeFailedTargetPath, setRemoveFailedTargetPath] = useState<string | null>(null);
  const [removingFailedTask, setRemovingFailedTask] = useState(false);

  const retryFailedTaskMutation = useMutation({
    mutationFn: (path: string) => queueApi.retryFailedTask(path),
    onSuccess: () => {
      toast.success("Queued for another attempt");
      void queryClient.invalidateQueries({ queryKey: ["failedOrganizeTasks"] });
    },
    onError: (err: unknown) => {
      toast.error(handleApiError(err).message);
    },
  });

  const handleRemoveFailedTask = async (path: string) => {
    setRemovingFailedTask(true);
    try {
      await queueApi.deleteFailedTask(path);
      toast.success("Removed from the organize queue");
      void queryClient.invalidateQueries({ queryKey: ["failedOrganizeTasks"] });
      setRemoveFailedTargetPath(null);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setRemovingFailedTask(false);
    }
  };

  const { dialogProps, checkCollisionAndProceed } = useTargetCollision({
    onReplaceExisting: (book) => proceedOrganizeDiscovered(book),
    onDeleteNew: (book) => {
      const path = book.fileInfo?.fullPath;
      if (path) {
        void executeDeleteDiscovered(path);
      }
    },
  });

  const handleOrganizeDiscovered = async (book: Audiobook) => {
    try {
      await checkCollisionAndProceed(book, proceedOrganizeDiscovered);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize) || 1;

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
            void handleStartScan();
          }}
          disabled={scanning}
        >
          <Scan className={`mr-2 h-4 w-4 ${scanning ? "animate-spin" : ""}`} />
          {scanning ? "Scanning Library..." : "Scan Library"}
        </Button>
      </div>

      <div>
        <h1 className="text-foreground text-2xl font-bold">Discovered Audiobooks ({totalCount})</h1>
        <p className="text-muted-foreground text-sm">
          Audiobook files found in the library directory that are not yet tracked in the database.
          Books marked "Well tagged" have valid tags and can be bulk imported.
        </p>
      </div>

      {failedTasks.length > 0 && (
        <Card className="border-destructive/50 p-4">
          <div className="mb-3 flex items-center gap-2">
            <AlertTriangle className="text-destructive h-4 w-4" />
            <h2 className="text-foreground text-sm font-semibold">
              Failed Organize Tasks ({failedTasks.length})
            </h2>
          </div>
          <p className="text-muted-foreground mb-3 text-xs">
            These files were queued to be organized but the queued data could not be read back -
            most likely left over from before an app update. Retry after a fix has shipped, or
            remove the queue entry and re-queue the file from scratch.
          </p>
          <div className="space-y-2">
            {failedTasks.map((task) => (
              <div
                key={task.originalFileLocation}
                className="border-border bg-card flex flex-col gap-2 rounded-md border p-2.5 sm:flex-row sm:items-center sm:justify-between"
              >
                <div className="min-w-0 flex-1">
                  <div className="text-foreground truncate text-sm font-medium">
                    {task.originalFileLocation}
                  </div>
                  <div className="text-muted-foreground text-xs">
                    Failed {task.failureCount} time{task.failureCount === 1 ? "" : "s"}
                    {task.lastFailureAt
                      ? ` · last at ${new Date(task.lastFailureAt).toLocaleString()}`
                      : ""}
                    {task.lastFailureReason ? ` · ${task.lastFailureReason}` : ""}
                  </div>
                </div>
                <div className="flex shrink-0 gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={
                      retryFailedTaskMutation.isPending &&
                      retryFailedTaskMutation.variables === task.originalFileLocation
                    }
                    onClick={() => retryFailedTaskMutation.mutate(task.originalFileLocation)}
                  >
                    <RotateCcw
                      className={`mr-1.5 h-3.5 w-3.5 ${
                        retryFailedTaskMutation.isPending &&
                        retryFailedTaskMutation.variables === task.originalFileLocation
                          ? "animate-spin"
                          : ""
                      }`}
                    />
                    Retry
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    className="text-destructive hover:text-destructive"
                    onClick={() => setRemoveFailedTargetPath(task.originalFileLocation)}
                  >
                    <Trash2 className="mr-1.5 h-3.5 w-3.5" />
                    Remove
                  </Button>
                </div>
              </div>
            ))}
          </div>
        </Card>
      )}

      {scanning && scanProgress && (
        <OperationProgressBar
          processed={scanProgress.scanned}
          total={scanProgress.total}
          label={scanProgress.message}
        />
      )}

      {scanResult && (
        <div className="border-primary/20 bg-primary/10 text-foreground rounded-lg border p-3 text-xs">
          Scan complete: {scanResult.newFilesDiscovered} new files discovered,{" "}
          {scanResult.alreadyTracked} already tracked.
        </div>
      )}

      {importing && importProgress && (
        <OperationProgressBar
          processed={importProgress.processed}
          total={importProgress.total}
          label={`Importing audiobooks (${importProgress.succeeded} succeeded, ${importProgress.failed} failed)`}
        />
      )}

      <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-center">
        <div className="relative max-w-md flex-1">
          <Search className="text-muted-foreground absolute top-2.5 left-3 h-4 w-4" />
          <Input
            placeholder="Filter by filename..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pr-9 pl-9"
          />
          {search ? (
            <button
              type="button"
              onClick={() => setSearch("")}
              aria-label="Clear filter"
              className="text-muted-foreground hover:text-foreground absolute top-2.5 right-2.5 cursor-pointer rounded-sm p-0.5 transition-colors"
            >
              <X className="h-4 w-4" />
            </button>
          ) : null}
        </div>

        {wellTaggedEligible.length > 0 && (
          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              className="w-full sm:w-auto"
              onClick={handleSelectAllWellTagged}
            >
              {selectedPaths.size === wellTaggedEligible.length ? (
                <CheckSquare className="mr-1.5 h-4 w-4" />
              ) : (
                <Square className="mr-1.5 h-4 w-4" />
              )}
              Select all well-tagged ({wellTaggedEligible.length})
            </Button>

            {selectedPaths.size > 0 && (
              <Button
                size="sm"
                className="w-full sm:w-auto"
                disabled={importing}
                onClick={() => {
                  void handleBulkImport();
                }}
              >
                <FolderInput className="mr-1.5 h-4 w-4" />
                Import Selected ({selectedPaths.size})
              </Button>
            )}
          </div>
        )}
      </div>

      {loading && books.length === 0 ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Loading discovered files...</p>
        </div>
      ) : books.length === 0 ? (
        <Card className="p-12 text-center">
          <FileAudio className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
          <h3 className="text-foreground text-lg font-medium">No discovered files</h3>
          <p className="text-muted-foreground mt-1 text-sm">
            Click "Scan Library" to search your library folder for untracked files.
          </p>
        </Card>
      ) : (
        <Accordion type="single" collapsible className="space-y-2">
          {books.map((book) => {
            const isSelected = selectedPaths.has(book.fullPath);
            const overrideKey = Object.keys(organizeOverrides).find((k) =>
              pathsEqual(k, book.fullPath),
            );
            const override = overrideKey ? organizeOverrides[overrideKey] : undefined;
            const isOrganizing = Boolean(override && override.error == null);
            const organizeError = override?.error;

            const initialAudiobook = toAudiobook(book);

            return (
              <AccordionItem
                key={book.fullPath}
                value={book.fullPath}
                className="border-border bg-card rounded-lg border px-4 shadow-sm"
              >
                <AccordionTrigger className="py-3 hover:no-underline">
                  <div className="flex min-w-0 flex-1 flex-wrap items-center justify-between gap-2.5 text-left">
                    <div className="flex min-w-0 flex-1 items-center gap-3">
                      {book.isWellTagged && !book.isDuplicate && (
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={(e) => {
                            e.stopPropagation();
                            handleToggleSelectPath(book.fullPath);
                          }}
                          onClick={(e) => e.stopPropagation()}
                          className="border-border h-4 w-4 shrink-0 rounded"
                        />
                      )}
                      <div className="min-w-0 flex-1">
                        <div className="text-foreground max-w-full min-w-0 truncate font-semibold">
                          {book.authors ? `${book.authors} — ` : ""}
                          {book.bookName || book.fileName}
                        </div>
                        <div className="text-muted-foreground flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
                          <span className="max-w-full min-w-0 truncate">{book.fileName}</span>
                          {(book.durationInSeconds || book.sizeInBytes > 0) && (
                            <div className="flex shrink-0 items-center gap-3">
                              {book.durationInSeconds && (
                                <span className="flex items-center gap-1">
                                  <Clock className="h-3 w-3" />
                                  {formatDuration(book.durationInSeconds)}
                                </span>
                              )}
                              {book.sizeInBytes > 0 && (
                                <span className="flex items-center gap-1">
                                  <HardDrive className="h-3 w-3" />
                                  {formatFileSize(book.sizeInBytes)}
                                </span>
                              )}
                            </div>
                          )}
                        </div>
                      </div>
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                      {book.isWellTagged ? (
                        <Badge
                          variant="secondary"
                          className="gap-1 bg-emerald-500/15 text-[11px] text-emerald-600 dark:text-emerald-400"
                        >
                          <CheckCircle2 className="h-3 w-3" />
                          Well tagged
                        </Badge>
                      ) : null}

                      {book.isDuplicate ? (
                        <Badge variant="destructive" className="gap-1 text-[11px]">
                          <AlertTriangle className="h-3 w-3" />
                          Duplicate target
                        </Badge>
                      ) : null}

                      {isOrganizing && (
                        <div className="w-full text-right sm:w-56">
                          {override?.progress != null ? (
                            <OperationProgressBar
                              compact
                              processed={override.progress}
                              total={100}
                              label={override.message || "Organizing..."}
                            />
                          ) : (
                            <span className="text-muted-foreground text-xs">
                              {override?.message || "Queued..."}
                            </span>
                          )}
                        </div>
                      )}

                      {organizeError && (
                        <Badge variant="destructive" className="gap-1 text-[11px]">
                          <AlertTriangle className="h-3 w-3" />
                          Organize failed
                        </Badge>
                      )}
                    </div>
                  </div>
                </AccordionTrigger>

                <AccordionContent className="border-border border-t pt-4 pb-4">
                  <div className="grid grid-cols-1 gap-6 lg:grid-cols-4">
                    <div className="space-y-6 lg:col-span-3">
                      <BookEditForm
                        initialBook={initialAudiobook}
                        currentPath={book.fullPath}
                        coverUrl={filesApi.getCoverUrl(book.fullPath)}
                        defaultEmptyLanguage
                        onSave={(edited) => handleOrganizeDiscovered(edited)}
                        onDelete={() => setDeleteTargetPath(book.fullPath)}
                        deleteLabel="Delete File"
                        submitLabel="Import to Library"
                        submitIcon={<FolderInput className="mr-2 h-4 w-4" />}
                      />
                    </div>

                    <div className="space-y-6 lg:col-span-1">
                      <AudiobookFileDetails
                        filePath={book.fullPath}
                        sizeInBytes={book.sizeInBytes}
                        durationInSeconds={book.durationInSeconds}
                      />
                    </div>
                  </div>
                </AccordionContent>
              </AccordionItem>
            );
          })}
        </Accordion>
      )}

      {dialogProps && <DuplicateTargetDialog {...dialogProps} />}

      {deleteTargetPath && (
        <DeleteFileDialog
          open={Boolean(deleteTargetPath)}
          onOpenChange={(open) => {
            if (!open) setDeleteTargetPath(null);
          }}
          targetPath={deleteTargetPath}
          onConfirmDelete={() => executeDeleteDiscovered(deleteTargetPath)}
          title="Delete Discovered Audiobook"
          description="Are you sure you want to permanently delete this file and its folder contents? This will remove the file from disk and remove its record from discovered audiobooks."
        />
      )}

      <Dialog
        open={Boolean(removeFailedTargetPath)}
        onOpenChange={(open) => {
          if (!open) setRemoveFailedTargetPath(null);
        }}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Remove Failed Organize Task</DialogTitle>
          </DialogHeader>
          <p className="text-muted-foreground text-sm">
            This removes the queue entry only - the file itself is untouched on disk. You can
            re-queue it from this page afterward.
          </p>
          <div className="border-border flex flex-col-reverse justify-end gap-2 border-t pt-4 sm:flex-row">
            <Button
              variant="outline"
              className="w-full sm:w-auto"
              onClick={() => setRemoveFailedTargetPath(null)}
              disabled={removingFailedTask}
            >
              Cancel
            </Button>
            <Button
              variant="destructive"
              className="w-full sm:w-auto"
              disabled={removingFailedTask}
              onClick={() => {
                if (removeFailedTargetPath) {
                  void handleRemoveFailedTask(removeFailedTargetPath);
                }
              }}
            >
              {removingFailedTask ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Removing...
                </>
              ) : (
                "Remove Task"
              )}
            </Button>
          </div>
        </DialogContent>
      </Dialog>

      {totalPages > 1 && (
        <div className="flex items-center justify-between pt-2">
          <span className="text-muted-foreground text-xs">
            Page {page} of {totalPages} ({totalCount} total)
          </span>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page <= 1 || loading}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={page >= totalPages || loading}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

export default DiscoveredAudiobooks;
