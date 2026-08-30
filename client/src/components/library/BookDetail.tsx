import { useState } from "react";
import { Link, useNavigate } from "@tanstack/react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft,
  Trash2,
  Save,
  Clock,
  HardDrive,
  FileText,
  AlertTriangle,
  CheckCircle2,
  RefreshCw,
  Loader2,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { BookEditForm } from "../BookEditForm";
import { DiffDisplay } from "../DiffDisplay";
import { browseApi, audiobookApi, consistencyApi, filesApi } from "@/services/api";
import { useSignalREvent, useSignalRReconnected } from "@/hooks/useSignalR";
import { formatDuration, formatFileSize } from "@/helpers/formatHelpers";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
import type { Audiobook } from "@/types/Audiobook";
import { Route } from "@/routes/library/book.$bookId";

interface SaveProgressPayload {
  audiobookId: number;
  progress: number;
  progressMessage: string;
}

interface SaveErrorPayload {
  audiobookId: number;
  error: string;
}

export function BookDetail() {
  const { bookId } = Route.useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const id = Number(bookId);

  const [saving, setSaving] = useState(false);
  const [saveProgress, setSaveProgress] = useState<number | null>(null);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);
  const [checkingConsistency, setCheckingConsistency] = useState(false);
  const [resolvingIssueId, setResolvingIssueId] = useState<number | null>(null);
  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const { data, isLoading: loading } = useQuery({
    queryKey: ["bookDetail", id],
    queryFn: async () => {
      const [detail, bookIssues] = await Promise.all([
        browseApi.getAudiobookDetail(id),
        consistencyApi.getIssuesByAudiobook(id).catch(() => []),
      ]);
      return { detail, bookIssues };
    },
    enabled: Boolean(id),
  });

  const bookDetail = data?.detail ?? null;
  const issues = data?.bookIssues ?? [];

  useSignalREvent<SaveProgressPayload>("AudiobookSaveProgress", (payload) => {
    if (payload.audiobookId === id) {
      setSaving(true);
      setSaveProgress(payload.progress);
      setSaveMessage(payload.progressMessage);
    }
  });

  useSignalREvent<{ audiobookId: number }>("AudiobookSaveComplete", (payload) => {
    if (payload.audiobookId === id) {
      setSaving(false);
      setSaveProgress(null);
      setSaveMessage(null);
      toast.success("Audiobook saved successfully");
      void queryClient.invalidateQueries({ queryKey: ["bookDetail", id] });
    }
  });

  useSignalREvent<SaveErrorPayload>("AudiobookSaveError", (payload) => {
    if (payload.audiobookId === id) {
      setSaving(false);
      setSaveProgress(null);
      toast.error(`Save error: ${payload.error}`);
    }
  });

  useSignalRReconnected(() => {
    if (!saving) return;
    void (async () => {
      try {
        const status = await audiobookApi.getSaveStatus(id);
        if (status.isSaving) return;
        setSaving(false);
        setSaveProgress(null);
        setSaveMessage(null);
        void queryClient.invalidateQueries({ queryKey: ["bookDetail", id] });
      } catch {
        // Keep existing state if status check fails
      }
    })();
  });

  const handleSave = async (updated: Audiobook) => {
    setSaving(true);
    try {
      await audiobookApi.updateBook(id, updated);
      toast.success("Update queued");
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setSaving(false);
    }
  };

  const handleCheckConsistency = async () => {
    setCheckingConsistency(true);
    try {
      await consistencyApi.recheckAudiobook(id);
      toast.success("Consistency check complete");
      void queryClient.invalidateQueries({ queryKey: ["bookDetail", id] });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setCheckingConsistency(false);
    }
  };

  const handleResolveIssue = async (issueId: number) => {
    setResolvingIssueId(issueId);
    try {
      await consistencyApi.resolveIssue(issueId);
      toast.success("Issue resolved");
      void queryClient.invalidateQueries({ queryKey: ["bookDetail", id] });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setResolvingIssueId(null);
    }
  };

  const handleDeleteBook = async () => {
    if (!bookDetail) return;
    setDeleting(true);
    try {
      await filesApi.deleteBook(bookDetail.filePath);
      toast.success("Audiobook deleted from library");
      void navigate({ to: "/library" });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setDeleting(false);
    }
  };

  if (loading) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-20">
        <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
        <p className="text-sm">Loading audiobook details...</p>
      </div>
    );
  }

  if (!bookDetail) {
    return (
      <div className="space-y-4 py-12 text-center">
        <h2 className="text-xl font-bold">Audiobook not found</h2>
        <Button render={<Link to="/library" />}>Back to Library</Button>
      </div>
    );
  }

  const initialAudiobook: Audiobook = {
    bookName: bookDetail.bookName ?? undefined,
    subtitle: bookDetail.subtitle ?? undefined,
    series: bookDetail.series ?? undefined,
    seriesPart: bookDetail.seriesPart ?? undefined,
    year: bookDetail.year ?? undefined,
    authors: bookDetail.authors.map((name) => ({ name })),
    narrators: bookDetail.narrators.map((name) => ({ name })),
    genres: bookDetail.genres,
    description: bookDetail.description ?? undefined,
    copyright: bookDetail.copyright ?? undefined,
    publisher: bookDetail.publisher ?? undefined,
    language: bookDetail.language ?? undefined,
    rating: bookDetail.rating ?? undefined,
    asin: bookDetail.asin ?? undefined,
    www: bookDetail.www ?? undefined,
    durationInSeconds: bookDetail.durationInSeconds ?? undefined,
    fileInfo: {
      fullPath: bookDetail.filePath,
      fileName: bookDetail.fileName,
      sizeInBytes: bookDetail.sizeInBytes,
    },
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <Button variant="ghost" size="sm" render={<Link to="/library" />}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </Button>

        <Button variant="destructive" size="sm" onClick={() => setDeleteConfirmOpen(true)}>
          <Trash2 className="mr-2 h-4 w-4" />
          Delete Audiobook
        </Button>
      </div>

      <div className="border-border flex flex-wrap items-center justify-between gap-4 border-b pb-4">
        <div>
          <h1 className="text-foreground text-2xl font-bold">
            {bookDetail.authors.join(", ")} &mdash; {bookDetail.bookName}
          </h1>
          <p className="text-muted-foreground text-sm">
            Edit metadata and examine audio file properties.
          </p>
        </div>

        {saving && (
          <div className="bg-muted text-muted-foreground flex items-center gap-2 rounded-md px-3 py-1.5 text-xs">
            <Loader2 className="text-primary h-4 w-4 animate-spin" />
            <span>
              {saveMessage || "Saving..."} {saveProgress != null ? `(${saveProgress}%)` : ""}
            </span>
          </div>
        )}
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-4">
        <div className="space-y-6 lg:col-span-3">
          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Metadata Editor</CardTitle>
            </CardHeader>
            <CardContent>
              <BookEditForm
                initialBook={initialAudiobook}
                currentPath={bookDetail.filePath}
                coverUrl={browseApi.getCoverUrl(id)}
                onSave={handleSave}
                formActions={
                  <Button type="submit" disabled={saving}>
                    {saving ? (
                      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    ) : (
                      <Save className="mr-2 h-4 w-4" />
                    )}
                    Save Changes
                  </Button>
                }
              />
            </CardContent>
          </Card>
        </div>

        <div className="space-y-6 lg:col-span-1">
          <Card>
            <CardHeader>
              <CardTitle className="text-muted-foreground text-sm font-semibold uppercase">
                Technical Details
              </CardTitle>
            </CardHeader>
            <CardContent className="space-y-3 text-xs">
              <div className="flex items-center gap-2">
                <Clock className="text-muted-foreground h-4 w-4" />
                <span className="text-foreground font-medium">Duration:</span>
                <span>
                  {bookDetail.durationInSeconds
                    ? formatDuration(bookDetail.durationInSeconds)
                    : "Unknown"}
                </span>
              </div>

              <div className="flex items-center gap-2">
                <HardDrive className="text-muted-foreground h-4 w-4" />
                <span className="text-foreground font-medium">File Size:</span>
                <span>{formatFileSize(bookDetail.sizeInBytes)}</span>
              </div>

              <div className="space-y-1">
                <div className="flex items-center gap-2">
                  <FileText className="text-muted-foreground h-4 w-4" />
                  <span className="text-foreground font-medium">File Path:</span>
                </div>
                <div
                  className="bg-muted/60 text-muted-foreground rounded p-2 font-mono text-[11px] break-all"
                  title={bookDetail.filePath}
                >
                  {bookDetail.filePath}
                </div>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle className="text-muted-foreground text-sm font-semibold uppercase">
                Consistency Issues
              </CardTitle>
              <Button
                variant="ghost"
                size="sm"
                className="h-7 text-xs"
                disabled={checkingConsistency}
                onClick={() => {
                  void handleCheckConsistency();
                }}
              >
                <RefreshCw
                  className={`mr-1 h-3.5 w-3.5 ${checkingConsistency ? "animate-spin" : ""}`}
                />
                Recheck
              </Button>
            </CardHeader>
            <CardContent className="space-y-3 text-xs">
              {issues.length === 0 ? (
                <div className="flex items-center gap-2 text-emerald-600 dark:text-emerald-400">
                  <CheckCircle2 className="h-4 w-4" />
                  <span>No consistency issues found.</span>
                </div>
              ) : (
                <div className="space-y-3">
                  {issues.map((issue) => (
                    <div
                      key={issue.id}
                      className="rounded-md border border-amber-500/20 bg-amber-500/10 p-2.5 text-xs text-amber-900 dark:text-amber-300"
                    >
                      <div className="flex items-center justify-between font-semibold">
                        <div className="flex items-center gap-1.5">
                          <AlertTriangle className="h-3.5 w-3.5" />
                          <span>{issue.issueType}</span>
                        </div>
                        <Button
                          size="sm"
                          variant="outline"
                          className="h-6 px-2 text-[10px]"
                          disabled={resolvingIssueId === issue.id}
                          onClick={() => {
                            void handleResolveIssue(issue.id);
                          }}
                        >
                          {resolvingIssueId === issue.id ? (
                            <Loader2 className="mr-1 h-2.5 w-2.5 animate-spin" />
                          ) : null}
                          Resolve
                        </Button>
                      </div>
                      <p className="mt-1 text-[11px] opacity-90">{issue.description}</p>

                      {issue.expectedValue && issue.actualValue ? (
                        <div className="mt-2">
                          <DiffDisplay expected={issue.expectedValue} actual={issue.actualValue} />
                        </div>
                      ) : null}
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </div>
      </div>

      <Dialog open={deleteConfirmOpen} onOpenChange={setDeleteConfirmOpen}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Delete Audiobook</DialogTitle>
          </DialogHeader>
          <div className="space-y-4 py-2">
            <p className="text-muted-foreground text-sm">
              Are you sure you want to permanently delete{" "}
              <strong className="text-foreground">{bookDetail.bookName}</strong>? This removes the
              audiobook directory and all its files from your library storage.
            </p>
            <div className="border-border flex justify-end gap-2 border-t pt-4">
              <Button variant="outline" onClick={() => setDeleteConfirmOpen(false)}>
                Cancel
              </Button>
              <Button
                variant="destructive"
                disabled={deleting}
                onClick={() => {
                  void handleDeleteBook();
                }}
              >
                {deleting ? "Deleting..." : "Delete Permanently"}
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

export default BookDetail;
