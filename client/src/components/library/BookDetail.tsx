import { useState } from "react";
import { Link, useNavigate, useRouter } from "@tanstack/react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, AlertTriangle, CheckCircle2, RefreshCw, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { BookEditForm } from "../BookEditForm";
import { DiffDisplay, TagMismatchDiffDisplay } from "../DiffDisplay";
import { DuplicateTargetDialog } from "../DuplicateTargetDialog";
import { DeleteFileDialog } from "../DeleteFileDialog";
import { AudiobookFileDetails } from "../AudiobookFileDetails";
import { browseApi, audiobookApi, consistencyApi, metadataRefreshApi } from "@/services/api";
import { SignalREvents } from "@/constants/signalrEvents";
import { useSignalREvent, useSignalRReconnected } from "@/hooks/useSignalR";
import { toAudiobook } from "@/helpers/audiobookMapping";
import { useTargetCollision } from "@/hooks/useTargetCollision";
import { handleApiError } from "@/lib/api";
import { notifyConsistencyResolveResult, getIssueTypeLabel } from "@/helpers/consistencyHelpers";
import { formatDateTime } from "@/helpers/formatHelpers";
import { pendingSnapshotToSearchResult } from "@/helpers/pendingMetadataRefresh";
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
  const router = useRouter();
  const queryClient = useQueryClient();
  const id = Number(bookId);

  const handleBack = () => {
    if (router.history.canGoBack()) {
      router.history.back();
    } else {
      void navigate({ to: "/library" });
    }
  };

  const [saving, setSaving] = useState(false);
  const [saveProgress, setSaveProgress] = useState<number | null>(null);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);
  const [checkingConsistency, setCheckingConsistency] = useState(false);
  const [resolvingIssueId, setResolvingIssueId] = useState<number | null>(null);
  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [pendingOpen, setPendingOpen] = useState(false);
  const [pendingApplied, setPendingApplied] = useState(false);

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

  // Pending refreshed-metadata snapshot for this book (the "review changes" banner). Absent =
  // nothing pending; a 404 from the endpoint means the same thing, and must not surface as an
  // error — the refresh bookkeeping timestamp is what matters for display.
  const { data: pending } = useQuery({
    queryKey: ["metadataRefresh", "pending", id],
    queryFn: () => metadataRefreshApi.getPendingForAudiobook(id),
    enabled: Boolean(id),
  });

  const bookDetail = data?.detail ?? null;
  const issues = data?.bookIssues ?? [];

  useSignalREvent<SaveProgressPayload>(SignalREvents.AudiobookSaveProgress, (payload) => {
    if (payload.audiobookId === id) {
      setSaving(true);
      setSaveProgress(payload.progress);
      setSaveMessage(payload.progressMessage);
    }
  });

  useSignalREvent<{ audiobookId: number }>(SignalREvents.AudiobookSaveComplete, (payload) => {
    if (payload.audiobookId === id) {
      setSaving(false);
      setSaveProgress(null);
      setSaveMessage(null);
      toast.success("Audiobook saved successfully");
      void queryClient.invalidateQueries({ queryKey: ["bookDetail", id] });
      void queryClient.invalidateQueries({ queryKey: ["metadataRefresh", "pending", id] });
      void queryClient.invalidateQueries({ queryKey: ["metadataRefresh", "pending-summary"] });
      if (pendingApplied) {
        setPendingApplied(false);
        void metadataRefreshApi.dismissPending(id).catch(() => {});
      }
    }
  });

  useSignalREvent<SaveErrorPayload>(SignalREvents.AudiobookSaveError, (payload) => {
    if (payload.audiobookId === id) {
      setSaving(false);
      setSaveProgress(null);
      // The save that carried an applied pending-refresh snapshot failed, so there is nothing
      // to dismiss — clear the arm so a later unrelated save can't dismiss it either.
      setPendingApplied(false);
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

  const proceedSave = async (updated: Audiobook) => {
    setSaving(true);
    // The pending-refresh marker rides on the audiobook object (survives the target-collision
    // dialog, which keeps the same book), so only an actual apply save arms the dismiss-after-
    // -completion flow. A cancelled dialog discards the object; a failed queue leaves the arm
    // clear. Either way the next unrelated save can't silently dismiss the stored snapshot.
    const applyingPendingRefresh = Boolean(updated.pendingRefreshApplied);
    try {
      await audiobookApi.updateBook(id, updated);
      if (applyingPendingRefresh) setPendingApplied(true);
      toast.success("Update queued");
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setSaving(false);
    }
  };

  const { dialogProps, checkCollisionAndProceed } = useTargetCollision({
    onReplaceExisting: (book) => proceedSave(book),
  });

  const handleSave = async (updated: Audiobook) => {
    try {
      await checkCollisionAndProceed(updated, proceedSave);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    }
  };

  // Invalidate every query whose result reflects this book's consistency status, so a
  // single-book recheck or resolve shows up on the Library Consistency page and in the
  // library list's issue badges. The book-detail query keeps its own id-scoped key.
  const invalidateConsistencyViews = () => {
    void queryClient.invalidateQueries({ queryKey: ["bookDetail", id] });
    void queryClient.invalidateQueries({ queryKey: ["consistency"] });
    void queryClient.invalidateQueries({ queryKey: ["books"] });
  };

  const handleCheckConsistency = async () => {
    setCheckingConsistency(true);
    try {
      await consistencyApi.recheckAudiobook(id);
      toast.success("Consistency check complete");
      invalidateConsistencyViews();
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setCheckingConsistency(false);
    }
  };

  const handleResolveIssue = async (issueId: number) => {
    setResolvingIssueId(issueId);
    try {
      const result = await consistencyApi.resolveIssue(issueId);
      notifyConsistencyResolveResult(result);
      if (result.actionTaken === "audiobook_deleted") {
        invalidateConsistencyViews();
        void navigate({ to: "/library" });
        return;
      }
      invalidateConsistencyViews();
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
      await audiobookApi.deleteAudiobook(id);
      toast.success("Audiobook deleted from library");
      void queryClient.invalidateQueries({ queryKey: ["books"] });
      void queryClient.invalidateQueries({ queryKey: ["bookDetail", id] });
      void navigate({ to: "/library" });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setDeleting(false);
    }
  };

  // Metadata refresh (single book, from its own source URL). The endpoint is synchronous and is
  // the backing surface for both "Refresh Now" and the consistency issue whose "resolve" means
  // retrying.
  const invalidateRefreshViews = () => {
    void queryClient.invalidateQueries({ queryKey: ["bookDetail", id] });
    void queryClient.invalidateQueries({ queryKey: ["metadataRefresh", "pending", id] });
    void queryClient.invalidateQueries({ queryKey: ["metadataRefresh", "pending-summary"] });
    // The library-list query folds the pending-summary into its badge computation, so a refresh
    // here must also invalidate it — the same reason handleDismissPending invalidates it below.
    void queryClient.invalidateQueries({ queryKey: ["books"] });
  };

  const handleRefreshNow = async () => {
    setRefreshing(true);
    try {
      const result = await metadataRefreshApi.refreshAudiobook(id);
      if (!result.success) {
        toast.error(result.error || "Metadata refresh failed");
      } else if (result.hasDifferences) {
        setPendingOpen(true);
        void queryClient.invalidateQueries({ queryKey: ["metadataRefresh", "pending", id] });
      } else {
        toast.success(`Metadata up to date (${result.sourceName ?? "source"})`);
      }
      invalidateRefreshViews();
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setRefreshing(false);
    }
  };

  const handleDismissPending = async () => {
    setPendingApplied(false);
    try {
      await metadataRefreshApi.dismissPending(id);
      toast.success("Pending metadata changes discarded");
      void queryClient.invalidateQueries({ queryKey: ["metadataRefresh", "pending", id] });
      void queryClient.invalidateQueries({ queryKey: ["metadataRefresh", "pending-summary"] });
      void queryClient.invalidateQueries({ queryKey: ["books"] });
      setPendingOpen(false);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
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

  const initialAudiobook = toAudiobook(bookDetail);
  const pendingRefreshResult = pending?.payload
    ? pendingSnapshotToSearchResult(pending.payload)
    : null;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <Button variant="ghost" size="sm" onClick={handleBack}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </Button>
      </div>

      <div className="border-border flex flex-wrap items-center justify-between gap-4 border-b pb-4">
        <div className="min-w-0 flex-1">
          <h1 className="text-foreground text-2xl font-bold break-words">
            {bookDetail.authors.join(", ")} &mdash; {bookDetail.bookName}
          </h1>
          <p className="text-muted-foreground text-sm">
            Edit metadata and examine audio file properties.
          </p>
          <p className="text-muted-foreground mt-1 text-xs">
            Last refreshed from source:{" "}
            {bookDetail.lastMetadataRefreshedAt
              ? formatDateTime(bookDetail.lastMetadataRefreshedAt)
              : "Never"}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              void handleRefreshNow();
            }}
            disabled={refreshing || saving || !bookDetail.www}
            className="text-xs"
            title={
              bookDetail.www
                ? "Re-fetch this book's metadata from its online source"
                : "This book has no source URL, so it cannot be refreshed"
            }
          >
            {refreshing ? (
              <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
            ) : (
              <RefreshCw className="mr-1.5 h-4 w-4" />
            )}
            {refreshing ? "Refreshing..." : "Refresh Now"}
          </Button>

          {saving && (
            <div className="bg-muted text-muted-foreground flex items-center gap-2 rounded-md px-3 py-1.5 text-xs">
              <Loader2 className="text-primary h-4 w-4 animate-spin" />
              <span>
                {saveMessage || "Saving..."} {saveProgress != null ? `(${saveProgress}%)` : ""}
              </span>
            </div>
          )}
        </div>
      </div>

      {pending && (
        <div className="border-border bg-primary/5 flex flex-wrap items-center justify-between gap-3 rounded-lg border p-3 text-sm">
          <div className="min-w-0">
            <p className="font-semibold">
              Pending metadata changes from {pending.sourceName ?? "online source"}
            </p>
            <p className="text-muted-foreground text-xs">
              Refreshed {formatDateTime(pending.fetchedAt)} — review the changes and save, or
              dismiss them.
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            <Button size="sm" onClick={() => setPendingOpen(true)}>
              Review Changes
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => {
                void handleDismissPending();
              }}
            >
              Dismiss
            </Button>
          </div>
        </div>
      )}

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
                onDelete={() => setDeleteConfirmOpen(true)}
                deleteLabel="Delete Audiobook"
                deleteDisabled={deleting}
                submitLabel="Save Changes"
                isSaving={saving}
                pendingRefreshResult={pendingRefreshResult}
                pendingRefreshOpen={pendingOpen}
                onPendingRefreshOpenChange={setPendingOpen}
              />
            </CardContent>
          </Card>
        </div>

        <div className="space-y-6 lg:col-span-1">
          <AudiobookFileDetails
            filePath={bookDetail.filePath}
            sizeInBytes={bookDetail.sizeInBytes}
            durationInSeconds={bookDetail.durationInSeconds}
          />

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
                          <span>{getIssueTypeLabel(issue.issueType)}</span>
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
                          {issue.issueType === "TagMismatch" ? (
                            <TagMismatchDiffDisplay
                              expected={issue.expectedValue}
                              actual={issue.actualValue}
                            />
                          ) : (
                            <DiffDisplay
                              expected={issue.expectedValue}
                              actual={issue.actualValue}
                            />
                          )}
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

      {dialogProps && <DuplicateTargetDialog {...dialogProps} />}

      <DeleteFileDialog
        open={deleteConfirmOpen}
        onOpenChange={setDeleteConfirmOpen}
        targetPath={bookDetail.filePath}
        onConfirmDelete={handleDeleteBook}
        title="Delete Audiobook"
        description={`Are you sure you want to permanently delete "${bookDetail.bookName}"? This removes the audiobook directory and all its files from your library storage.`}
      />
    </div>
  );
}

export default BookDetail;
