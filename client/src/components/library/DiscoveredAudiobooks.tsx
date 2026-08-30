import { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft,
  Search,
  Scan,
  CheckSquare,
  Square,
  FolderInput,
  Loader2,
  FileAudio,
  CheckCircle2,
  AlertTriangle,
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
import { OperationProgressBar } from "@/components/OperationProgressBar";
import { BookEditForm } from "@/components/BookEditForm";
import { libraryApi, audiobookApi } from "@/services/api";
import { useSignalREvent, useSignalRReconnected } from "@/hooks/useSignalR";
import { handleApiError } from "@/lib/api";
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
    setOrganizeOverrides((prev) => ({
      ...prev,
      [payload.originalFileLocation]: {
        progress: payload.progress,
        message: payload.progressMessage,
      },
    }));

    if (payload.progress >= 100) {
      void queryClient.invalidateQueries({
        queryKey: ["discoveredAudiobooks"],
      });
    }
  });

  useSignalREvent<OrganizeQueueErrorPayload>("QueueError", (payload) => {
    setOrganizeOverrides((prev) => ({
      ...prev,
      [payload.originalFileLocation]: {
        error: payload.error,
      },
    }));
    toast.error(`Organize failed: ${payload.error}`);
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

  const handleDeleteDiscovered = async (path: string) => {
    try {
      await libraryApi.deleteDiscovered(path);
      toast.success("File record removed");
      void queryClient.invalidateQueries({
        queryKey: ["discoveredAudiobooks"],
      });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    }
  };

  const handleOrganizeDiscovered = async (path: string, book: Audiobook) => {
    try {
      await audiobookApi.organizeBook(book);
      toast.success("Book added to organization queue");
      setOrganizeOverrides((prev) => ({
        ...prev,
        [path]: { progress: 0, message: "Queued..." },
      }));
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

      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="relative max-w-md flex-1">
          <Search className="text-muted-foreground absolute top-2.5 left-3 h-4 w-4" />
          <Input
            placeholder="Filter by filename..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-9"
          />
        </div>

        {wellTaggedEligible.length > 0 && (
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={handleSelectAllWellTagged}>
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
            const override = organizeOverrides[book.fullPath];
            const isOrganizing = Boolean(override && override.error == null);
            const organizeError = override?.error;

            const initialAudiobook: Audiobook = {
              authors: book.authors,
              narrators: book.narrators,
              bookName: book.bookName,
              series: book.series,
              seriesPart: book.seriesPart,
              year: book.year,
              genres: book.genres,
              description: book.description,
              copyright: book.copyright,
              publisher: book.publisher,
              language: book.language,
              rating: book.rating,
              asin: book.asin,
              www: book.www,
              fileInfo: book.fileInfo,
            };

            return (
              <AccordionItem
                key={book.fullPath}
                value={book.fullPath}
                className="border-border bg-card rounded-lg border px-4 shadow-sm"
              >
                <AccordionTrigger className="py-3 hover:no-underline">
                  <div className="flex w-full flex-wrap items-center justify-between gap-3 pr-4 text-left">
                    <div className="flex min-w-0 items-center gap-3">
                      {book.isWellTagged && !book.isDuplicate && (
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={(e) => {
                            e.stopPropagation();
                            handleToggleSelectPath(book.fullPath);
                          }}
                          onClick={(e) => e.stopPropagation()}
                          className="border-border h-4 w-4 rounded"
                        />
                      )}
                      <div className="min-w-0 truncate">
                        <div className="text-foreground truncate font-semibold">
                          {book.authors?.length
                            ? `${book.authors.map((a) => a.name).join(", ")} — `
                            : ""}
                          {book.bookName || book.filename}
                        </div>
                        <div className="text-muted-foreground truncate text-xs">
                          {book.filename}
                        </div>
                      </div>
                    </div>

                    <div className="flex shrink-0 items-center gap-2">
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
                        <div className="w-36 text-right">
                          {override?.progress != null ? (
                            <OperationProgressBar
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
                  <div className="space-y-4">
                    <div className="flex justify-end gap-2">
                      <Button
                        variant="destructive"
                        size="sm"
                        onClick={() => {
                          void handleDeleteDiscovered(book.fullPath);
                        }}
                      >
                        <Trash2 className="mr-1.5 h-3.5 w-3.5" />
                        Dismiss / Delete
                      </Button>
                    </div>

                    <BookEditForm
                      initialBook={initialAudiobook}
                      currentPath={book.fullPath}
                      onSave={(edited) => handleOrganizeDiscovered(book.fullPath, edited)}
                      formActions={
                        <Button type="submit">
                          <FolderInput className="mr-2 h-4 w-4" />
                          Import to Library
                        </Button>
                      }
                    />
                  </div>
                </AccordionContent>
              </AccordionItem>
            );
          })}
        </Accordion>
      )}

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
