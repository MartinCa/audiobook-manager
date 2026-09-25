import { useRef, useState, type ReactNode } from "react";
import { Link, useNavigate, useRouterState, useSearch } from "@tanstack/react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft,
  AlertTriangle,
  CheckCircle2,
  Image as ImageIcon,
  RefreshCw,
  Loader2,
  Pencil,
  Search,
} from "lucide-react";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { BookEditForm } from "../BookEditForm";
import { LinkButton } from "../LinkButton";
import { DiffDisplay, TagMismatchDiffDisplay } from "../DiffDisplay";
import { DuplicateTargetDialog } from "../DuplicateTargetDialog";
import { DeleteFileDialog } from "../DeleteFileDialog";
import { AudiobookFileDetails } from "../AudiobookFileDetails";
import {
  browseApi,
  audiobookApi,
  consistencyApi,
  metadataRefreshApi,
  settingsApi,
} from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { SignalREvents } from "@/constants/signalrEvents";
import { useSignalREvent, useSignalRReconnected } from "@/hooks/useSignalR";
import { toAudiobook } from "@/helpers/audiobookMapping";
import { languageLabel } from "@/helpers/languages";
import { useTargetCollision } from "@/hooks/useTargetCollision";
import { handleApiError } from "@/lib/api";
import {
  notifyBookConsistencyResolveResult,
  getIssueTypeLabel,
} from "@/helpers/consistencyHelpers";
import { formatDateTime } from "@/helpers/formatHelpers";
import { pendingSnapshotToSearchResult } from "@/helpers/pendingMetadataRefresh";
import { notifications } from "@/lib/notifications";
import { cn } from "@/lib/utils";
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

export interface BookDetailProps {
  /**
   * "view" = read-only book page with author/series links; "edit" = the existing BookEditForm
   * and its save/delete/refresh behavior. Omit to derive the mode from the matched route
   * (/library/book/$bookId is read-only, /library/book/$bookId/edit is the editor) - the two
   * routes render this same component and the router passes no props to route components.
   */
  mode?: "view" | "edit";
}

export function BookDetail({ mode }: BookDetailProps) {
  const { bookId } = Route.useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const id = Number(bookId);
  // Route components receive no props (the TanStack code-splitter drops inline wrappers), so the
  // mode is derived from the matched path instead. The prop stays as an explicit override for
  // direct renders/tests.
  const isEditMode = (mode ?? (pathname.endsWith("/edit") ? "edit" : "view")) === "edit";
  // Only the edit route declares this search param; `strict: false` reads whatever the currently
  // matched route validated instead of requiring this component to be mounted under one specific
  // route (BookDetail backs both /library/book/$bookId and its /edit sibling).
  const { openSearch } = useSearch({ strict: false });
  // Armed in proceedSave when the saved object carries the autoSavedFromSearch marker (a "Search
  // Online Metadata" apply with the opt-out toggle left off) and the queue call actually
  // succeeded — tells the AudiobookSaveComplete handler below to route back to the view page once
  // that save lands, matching the read-only page's search entry point round-tripping back once
  // metadata is applied directly. Only armed after a real save is in flight, not merely requested:
  // a cancelled target-collision dialog or a failed zod validation never reaches proceedSave, so
  // it never leaves this armed for whatever unrelated save happens to complete next.
  const returnToViewAfterSaveRef = useRef(false);

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
    queryKey: queryKeys.bookDetail(id),
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
    queryKey: queryKeys.metadataRefresh.pendingForBook(id),
    // The endpoint 404s when nothing is pending; the API layer normalizes that to undefined, and
    // the query layer maps it to null (queryFn must not resolve undefined) - both read as absent.
    queryFn: () => metadataRefreshApi.getPendingForAudiobook(id).then((pending) => pending ?? null),
    enabled: Boolean(id),
  });

  const { data: languagesRes } = useQuery({
    queryKey: queryKeys.languages(),
    queryFn: () => settingsApi.getLanguages(),
    enabled: Boolean(id) && !isEditMode,
  });
  const languages = languagesRes?.languages ?? [];

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
      notifications.success("Audiobook saved successfully");
      void queryClient.invalidateQueries({ queryKey: queryKeys.bookDetail(id) });
      void queryClient.invalidateQueries({
        queryKey: queryKeys.metadataRefresh.pendingForBook(id),
      });
      void queryClient.invalidateQueries({ queryKey: queryKeys.metadataRefresh.pendingSummary() });
      // A saved book may have changed Series/SeriesPart (directly, or a series-part edit); the
      // series detail page's owned-books roster is a separate query family and would otherwise
      // stay stale for its 30s staleTime - same reasoning as the bulk-edit completion handler in
      // BookBulkActionBar, which invalidates this same family for the same books-changed reason.
      void queryClient.invalidateQueries({ queryKey: queryKeys.seriesDetail.all() });
      // A save is exactly how a book missing a field gets that field filled in - same reasoning
      // as BookBulkActionBar's invalidateCommonViews.
      void queryClient.invalidateQueries({ queryKey: queryKeys.missingTagsAudiobooks.all() });
      if (pendingApplied) {
        setPendingApplied(false);
        void metadataRefreshApi.dismissPending(id).catch(() => {});
      }
      if (returnToViewAfterSaveRef.current) {
        returnToViewAfterSaveRef.current = false;
        void navigate({ to: "/library/book/$bookId", params: { bookId } });
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
      // Same reasoning: a failed save leaves nothing to return to the view page for, and the
      // user needs to stay on the edit page to see why it failed.
      returnToViewAfterSaveRef.current = false;
      notifications.error(`Save error: ${payload.error}`);
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
        void queryClient.invalidateQueries({ queryKey: queryKeys.bookDetail(id) });
      } catch {
        // Keep existing state if status check fails
      }
    })();
  });

  const proceedSave = async (updated: Audiobook) => {
    setSaving(true);
    // Both markers ride on the audiobook object (survives the target-collision dialog, which
    // keeps the same book), so only an actual apply save arms their respective after-completion
    // behavior. A cancelled dialog discards the object; a failed queue leaves both arms clear.
    // Either way, an unrelated save can't inherit either flow.
    const applyingPendingRefresh = Boolean(updated.pendingRefreshApplied);
    const isAutoSaveFromSearch = Boolean(updated.autoSavedFromSearch);
    try {
      await audiobookApi.updateBook(id, updated);
      if (applyingPendingRefresh) setPendingApplied(true);
      if (isAutoSaveFromSearch) returnToViewAfterSaveRef.current = true;
      notifications.success("Update queued");
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
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
      notifications.error(handleApiError(err).message);
    }
  };

  // Invalidate every query whose result reflects this book's consistency status, so a
  // single-book recheck or resolve shows up on the Library Consistency page and in the
  // library list's issue badges. The book-detail query keeps its own id-scoped key.
  const invalidateConsistencyViews = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.bookDetail(id) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.consistency.all() });
    void queryClient.invalidateQueries({ queryKey: queryKeys.books.all() });
    // A TagMismatch/WrongFilePath resolve rewrites the book's tags in full (AudiobookService.
    // UpdateAudiobook), which can fill in a field this book was missing.
    void queryClient.invalidateQueries({ queryKey: queryKeys.missingTagsAudiobooks.all() });
  };

  const handleCheckConsistency = async () => {
    setCheckingConsistency(true);
    try {
      await consistencyApi.recheckAudiobook(id);
      notifications.success("Consistency check complete");
      invalidateConsistencyViews();
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setCheckingConsistency(false);
    }
  };

  const handleResolveIssue = async (issueId: number) => {
    setResolvingIssueId(issueId);
    try {
      const result = await consistencyApi.resolveIssue(issueId);
      notifyBookConsistencyResolveResult(result);
      if (result.actionTaken === "audiobook_deleted") {
        invalidateConsistencyViews();
        void navigate({ to: "/library" });
        return;
      }
      invalidateConsistencyViews();
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setResolvingIssueId(null);
    }
  };

  const handleDeleteBook = async () => {
    if (!bookDetail) return;
    setDeleting(true);
    try {
      await audiobookApi.deleteAudiobook(id);
      notifications.success("Audiobook deleted from library");
      void queryClient.invalidateQueries({ queryKey: queryKeys.books.all() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.bookDetail(id) });
      void navigate({ to: "/library" });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
      setDeleting(false);
    }
  };

  // Metadata refresh (single book, from its own source URL). The endpoint is synchronous and is
  // the backing surface for both "Refresh Now" and the consistency issue whose "resolve" means
  // retrying.
  const invalidateRefreshViews = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.bookDetail(id) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.metadataRefresh.pendingForBook(id) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.metadataRefresh.pendingSummary() });
    // The library-list query folds the pending-summary into its badge computation, so a refresh
    // here must also invalidate it — the same reason handleDismissPending invalidates it below.
    void queryClient.invalidateQueries({ queryKey: queryKeys.books.all() });
  };

  const handleRefreshNow = async () => {
    setRefreshing(true);
    try {
      const result = await metadataRefreshApi.refreshAudiobook(id);
      if (!result.success) {
        notifications.error(result.error || "Metadata refresh failed");
      } else if (result.hasDifferences) {
        setPendingOpen(true);
        void queryClient.invalidateQueries({
          queryKey: queryKeys.metadataRefresh.pendingForBook(id),
        });
      } else {
        notifications.success(`Metadata up to date (${result.sourceName ?? "source"})`);
      }
      invalidateRefreshViews();
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setRefreshing(false);
    }
  };

  const handleDismissPending = async () => {
    setPendingApplied(false);
    try {
      await metadataRefreshApi.dismissPending(id);
      notifications.success("Pending metadata changes discarded");
      void queryClient.invalidateQueries({
        queryKey: queryKeys.metadataRefresh.pendingForBook(id),
      });
      void queryClient.invalidateQueries({ queryKey: queryKeys.metadataRefresh.pendingSummary() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.books.all() });
      setPendingOpen(false);
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
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
        <LinkButton render={<Link to="/library" />}>Back to Library</LinkButton>
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
        <LinkButton variant="ghost" size="sm" render={<Link to="/library" />} className="text-xs">
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </LinkButton>
      </div>

      <div className="border-border flex flex-wrap items-center justify-between gap-4 border-b pb-4">
        <div className="min-w-0 flex-1">
          <h1 className="text-foreground text-2xl font-bold break-words">
            {bookDetail.authors.join(", ")} &mdash; {bookDetail.bookName}
          </h1>
          <p className="text-muted-foreground text-sm">
            {isEditMode ? "Edit metadata and examine audio file properties." : "Audiobook details."}
          </p>
          <p className="text-muted-foreground mt-1 text-xs">
            Last refreshed from source:{" "}
            {bookDetail.lastMetadataRefreshedAt
              ? formatDateTime(bookDetail.lastMetadataRefreshedAt)
              : "Never"}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {isEditMode && (
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
          )}

          {!isEditMode && (
            <Button
              size="sm"
              variant="outline"
              onClick={() =>
                void navigate({
                  to: "/library/book/$bookId/edit",
                  params: { bookId },
                  search: { openSearch: true },
                })
              }
            >
              <Search className="mr-1.5 h-4 w-4" />
              Search Online Metadata
            </Button>
          )}

          {isEditMode ? (
            <Button
              size="sm"
              variant="outline"
              onClick={() => void navigate({ to: "/library/book/$bookId", params: { bookId } })}
            >
              Done
            </Button>
          ) : (
            <Button
              size="sm"
              onClick={() =>
                void navigate({ to: "/library/book/$bookId/edit", params: { bookId } })
              }
            >
              <Pencil className="mr-1.5 h-4 w-4" />
              Edit
            </Button>
          )}

          {saving && isEditMode && (
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
            {isEditMode ? (
              <Button size="sm" onClick={() => setPendingOpen(true)}>
                Review Changes
              </Button>
            ) : (
              // A real link, not a button: on the read-only page this navigates into the editor.
              <Link
                to="/library/book/$bookId/edit"
                params={{ bookId }}
                className={cn(buttonVariants({ size: "sm" }))}
              >
                Review Changes
              </Link>
            )}
            {isEditMode && (
              <Button
                size="sm"
                variant="outline"
                onClick={() => {
                  void handleDismissPending();
                }}
              >
                Dismiss
              </Button>
            )}
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-4">
        <div className="space-y-6 lg:col-span-3">
          {isEditMode ? (
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
                  currentBookId={id}
                  autoOpenSearchDialog={openSearch === true}
                />
              </CardContent>
            </Card>
          ) : (
            <Card>
              <CardHeader>
                <CardTitle className="text-lg">Book Details</CardTitle>
              </CardHeader>
              <CardContent className="flex flex-col gap-6 text-sm sm:flex-row sm:items-start">
                <BookCover
                  coverUrl={bookDetail.coverFilePath ? browseApi.getCoverUrl(id) : undefined}
                  alt={bookDetail.bookName ?? "Cover"}
                />
                <div className="grid min-w-0 flex-1 grid-cols-1 gap-x-8 gap-y-3 sm:grid-cols-2">
                  <DetailRow label="Authors">
                    {bookDetail.authorRefs.length > 0 ? (
                      <span className="flex flex-wrap gap-x-2 gap-y-1">
                        {bookDetail.authorRefs.map((author) => (
                          <Link
                            key={author.id}
                            to="/library/authors/$authorId"
                            params={{ authorId: String(author.id) }}
                            className="text-primary font-medium hover:underline"
                          >
                            {author.name}
                          </Link>
                        ))}
                      </span>
                    ) : (
                      <span className="text-muted-foreground">Unknown</span>
                    )}
                  </DetailRow>

                  <DetailRow label="Narrators">
                    <span className="break-words">
                      {bookDetail.narrators.length > 0 ? bookDetail.narrators.join(", ") : "None"}
                    </span>
                  </DetailRow>

                  <DetailRow label="Book name">{bookDetail.bookName}</DetailRow>

                  <DetailRow label="Subtitle">{bookDetail.subtitle || "—"}</DetailRow>

                  <DetailRow label="Series">
                    {bookDetail.series ? (
                      <span className="flex flex-wrap items-center gap-x-2">
                        <Link
                          to="/library/series/$seriesName"
                          params={{ seriesName: bookDetail.series }}
                          className="text-primary font-medium hover:underline"
                        >
                          {bookDetail.series}
                        </Link>
                        {bookDetail.seriesPart && (
                          <span className="text-muted-foreground">
                            · part {bookDetail.seriesPart}
                          </span>
                        )}
                      </span>
                    ) : (
                      <span className="text-muted-foreground">None</span>
                    )}
                  </DetailRow>

                  <DetailRow label="Year">
                    {bookDetail.year ? String(bookDetail.year) : "Unknown"}
                  </DetailRow>

                  <DetailRow label="Genres">
                    <span className="break-words">
                      {bookDetail.genres.length > 0 ? bookDetail.genres.join(", ") : "None"}
                    </span>
                  </DetailRow>

                  <DetailRow label="Language">
                    {languageLabel(bookDetail.language, languages) || "—"}
                  </DetailRow>

                  <DetailRow label="Description">
                    <span className="text-muted-foreground break-words whitespace-pre-wrap">
                      {bookDetail.description || "No description."}
                    </span>
                  </DetailRow>

                  <DetailRow label="Rating">{bookDetail.rating || "—"}</DetailRow>

                  {bookDetail.www && (
                    <DetailRow label="Web link">
                      <a
                        href={bookDetail.www}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="text-primary break-all hover:underline"
                      >
                        {bookDetail.www}
                      </a>
                    </DetailRow>
                  )}

                  <DetailRow label="Publisher">{bookDetail.publisher || "—"}</DetailRow>

                  <DetailRow label="Copyright">{bookDetail.copyright || "—"}</DetailRow>

                  <DetailRow label="ASIN">{bookDetail.asin || "—"}</DetailRow>
                </div>
              </CardContent>
            </Card>
          )}
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
              {isEditMode && (
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
              )}
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
                        {isEditMode && (
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
                        )}
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

/**
 * Read-only cover on the view page - the edit page shows the same image through CoverEditor.
 * A book with no cover on disk (or a cover URL that fails to load) falls back to a placeholder
 * rather than a broken-image icon.
 */
function BookCover({ coverUrl, alt }: { coverUrl: string | undefined; alt: string }) {
  const [failedUrl, setFailedUrl] = useState<string | undefined>(undefined);
  const src = coverUrl && coverUrl !== failedUrl ? coverUrl : undefined;

  return (
    <div className="border-border bg-muted flex h-48 w-48 shrink-0 items-center justify-center self-center overflow-hidden rounded-lg border sm:self-start">
      {src ? (
        <img
          src={src}
          alt={alt}
          className="h-full w-full object-contain"
          onError={() => setFailedUrl(src)}
        />
      ) : (
        <div className="text-muted-foreground flex flex-col items-center p-4 text-center">
          <ImageIcon className="mb-2 h-10 w-10" />
          <span className="text-xs font-medium">No cover</span>
        </div>
      )}
    </div>
  );
}

/** One labelled read-only metadata row on the book detail page. */
function DetailRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0 flex-1">
      <div className="text-muted-foreground text-xs font-semibold uppercase">{label}</div>
      <div className="mt-0.5">{children}</div>
    </div>
  );
}

export default BookDetail;
