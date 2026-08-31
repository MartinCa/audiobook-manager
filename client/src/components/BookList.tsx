import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { FolderInput, RefreshCw, HardDrive, AlertCircle, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { OperationProgressBar } from "./OperationProgressBar";
import { BookOrganize } from "./BookOrganize";
import { untaggedApi, queueApi } from "@/services/api";
import { useSignalREvent, useSignalRReconnected } from "@/hooks/useSignalR";
import { formatFileSize } from "@/helpers/formatHelpers";
import { pathsEqual } from "@/helpers/pathHelpers";
import { toast } from "sonner";
import type { BookFileInfo } from "@/types/BookFileInfo";

interface ProgressUpdatePayload {
  originalFileLocation: string;
  progress: number;
  progressMessage: string;
}

interface QueueErrorPayload {
  originalFileLocation: string;
  error: string;
}

export function BookList() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const [activeItem, setActiveItem] = useState<string | undefined>(undefined);

  // Live real-time progress overrides from SignalR
  const [progressOverrides, setProgressOverrides] = useState<
    Record<string, { progress?: number; message?: string; error?: string }>
  >({});

  const {
    data,
    isLoading: loading,
    refetch,
  } = useQuery({
    queryKey: ["untaggedBooks", page, pageSize],
    queryFn: async () => {
      const [untaggedRes, queuedPaths] = await Promise.all([
        untaggedApi.getUntagged(pageSize, (page - 1) * pageSize),
        queueApi.getQueuedBooks().catch(() => [] as string[]),
      ]);

      const queuedSet = new Set(queuedPaths);
      const items = untaggedRes.items.map((item) => ({
        ...item,
        queueId: queuedSet.has(item.fullPath) ? item.fullPath : item.queueId,
        queueMessage: queuedSet.has(item.fullPath) ? "Queued..." : item.queueMessage,
      }));

      return { items, total: untaggedRes.total };
    },
  });

  const books: BookFileInfo[] = data?.items ?? [];
  const totalCount = data?.total ?? 0;

  useSignalREvent<ProgressUpdatePayload>("UpdateProgress", (payload) => {
    setProgressOverrides((prev) => {
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
      void queryClient.invalidateQueries({ queryKey: ["untaggedBooks"] });
    }
  });

  useSignalREvent<QueueErrorPayload>("QueueError", (payload) => {
    setProgressOverrides((prev) => {
      const next = { ...prev };
      const matchedKey =
        Object.keys(next).find((k) => pathsEqual(k, payload.originalFileLocation)) ??
        payload.originalFileLocation;
      next[matchedKey] = {
        error: payload.error,
      };
      return next;
    });
    toast.error(`Queue error: ${payload.error}`);
  });

  // A dropped/re-established connection may have missed progress events for items already
  // queued elsewhere; re-syncing the list re-derives queue state the same way it does on mount.
  useSignalRReconnected(() => {
    void refetch();
  });

  const totalPages = Math.ceil(totalCount / pageSize) || 1;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
            <FolderInput className="text-primary h-6 w-6" />
            Organize Audiobooks
          </h1>
          <p className="text-muted-foreground text-sm">
            Process unorganized audio files into your structured library.
          </p>
        </div>

        <Button
          variant="outline"
          size="sm"
          onClick={() => {
            void refetch();
          }}
          disabled={loading}
        >
          <RefreshCw className={`mr-2 h-4 w-4 ${loading ? "animate-spin" : ""}`} />
          Reload
        </Button>
      </div>

      <div className="text-muted-foreground text-xs">
        Showing {books.length} of {totalCount} files to organize
      </div>

      {loading && books.length === 0 ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-20">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Loading files from import directory...</p>
        </div>
      ) : books.length === 0 ? (
        <Card className="p-12 text-center">
          <FolderInput className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
          <h3 className="text-foreground text-lg font-medium">No audiobooks to organize</h3>
          <p className="text-muted-foreground mt-1 text-sm">
            Place audiobook files in your input directory and reload to organize them.
          </p>
        </Card>
      ) : (
        <Accordion
          type="single"
          collapsible
          value={activeItem}
          onValueChange={setActiveItem}
          className="space-y-2"
        >
          {books.map((book) => {
            const overrideKey = Object.keys(progressOverrides).find((k) =>
              pathsEqual(k, book.fullPath),
            );
            const override = overrideKey ? progressOverrides[overrideKey] : undefined;
            const isQueued = Boolean(book.queueId || override?.progress != null);
            const progress = override?.progress ?? book.queueProgress;
            const message = override?.message ?? book.queueMessage;
            const error = override?.error ?? book.error;

            return (
              <AccordionItem
                key={book.fullPath}
                value={book.fullPath}
                className="border-border bg-card overflow-hidden rounded-lg border shadow-sm"
              >
                <div className="flex items-center justify-between px-4 py-3">
                  <AccordionTrigger className="min-w-0 flex-1 py-0 text-left hover:no-underline">
                    <div className="flex min-w-0 flex-1 items-center gap-3 pr-4">
                      <div className="bg-primary/10 text-primary flex h-9 w-9 shrink-0 items-center justify-center rounded-md">
                        <FolderInput className="h-4 w-4" />
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="text-foreground truncate text-sm font-semibold">
                          {book.fileName}
                        </div>
                        <div className="text-muted-foreground flex items-center gap-3 text-xs">
                          <span className="flex items-center gap-1">
                            <HardDrive className="h-3 w-3" />
                            {formatFileSize(book.sizeInBytes)}
                          </span>
                        </div>
                      </div>
                    </div>
                  </AccordionTrigger>

                  <div className="flex shrink-0 items-center gap-2">
                    {isQueued && (
                      <div className="w-36 text-right">
                        {progress != null && (
                          <OperationProgressBar
                            processed={progress}
                            total={100}
                            label={message || "Organizing..."}
                          />
                        )}
                        {message && !progress && (
                          <span className="text-muted-foreground text-xs">{message}</span>
                        )}
                      </div>
                    )}

                    {error && (
                      <span className="text-destructive flex items-center gap-1 text-xs">
                        <AlertCircle className="h-3 w-3" />
                        Failed
                      </span>
                    )}
                  </div>
                </div>

                <AccordionContent className="border-border bg-muted/20 border-t p-4">
                  <BookOrganize
                    file={book}
                    onSuccess={() => {
                      setActiveItem(undefined);
                      void queryClient.invalidateQueries({
                        queryKey: ["untaggedBooks"],
                      });
                    }}
                  />
                </AccordionContent>
              </AccordionItem>
            );
          })}
        </Accordion>
      )}

      {totalPages > 1 && (
        <div className="border-border flex items-center justify-between border-t pt-4">
          <div className="text-muted-foreground text-xs">
            Page {page} of {totalPages}
          </div>
          <div className="flex items-center gap-2">
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

export default BookList;
