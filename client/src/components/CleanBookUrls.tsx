import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Link2, Loader2, Sparkles } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Card } from "@/components/ui/card";
import { urlCleanupApi } from "@/services/api";
import type { AudiobookUrlCleanup } from "@/types/UrlCleanup";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";

const PAGE_SIZE = 50;

export function CleanBookUrls() {
  const queryClient = useQueryClient();

  // The dirty list is paged server-side: with a few thousand dirty URLs the old unpaged
  // response rendered every book as a card into the DOM and the page became unusably slow.
  const [page, setPage] = useState(0);

  const {
    data: pageData,
    isLoading,
    isFetching,
  } = useQuery({
    queryKey: ["urlCleanup", "page", page],
    // keepPreviousData: while the next page loads the previous one stays rendered (with the
    // checkboxes dimmed via isFetching), so the pager doesn't vanish on every navigation.
    placeholderData: keepPreviousData,
    queryFn: () => urlCleanupApi.getDirtyUrlPage(page, PAGE_SIZE),
  });

  const totalCount = pageData?.totalCount ?? 0;
  const dirtyUrls = (pageData?.items ?? []) as AudiobookUrlCleanup[];

  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  // Clamped here rather than only where the pager is drawn, so the page that is *fetched* and the
  // page that is *displayed* can never disagree (same fix as LibraryConsistency's pager).
  const currentPage = Math.min(page, pageCount - 1);

  // null means "not customized yet" - defaults to everything on the loaded page selected. Once
  // the user toggles a box we switch to an explicit set, which is simpler than syncing state off
  // the query result.
  const [customSelection, setCustomSelection] = useState<Set<number> | null>(null);
  const selectedIds = customSelection ?? new Set(dirtyUrls.map((b) => b.audiobookId));

  // The selection is page-scoped: navigating resets it back to "everything on the visible page
  // selected", so the button label always describes what is in front of the user. Hoisted out of
  // the pager buttons so the same reset also covers apply (below).
  const goToPage = (next: number) => {
    setPage(next);
    setCustomSelection(null);
  };

  const applyMutation = useMutation({
    mutationFn: (audiobookIds: number[]) => urlCleanupApi.apply(audiobookIds),
    onSuccess: (result) => {
      toast.success(`Cleaned ${result.updated ?? 0} book URL${result.updated === 1 ? "" : "s"}`);
      // Drop back to page 0 too: whatever page was being looked at may now be a stale slice.
      goToPage(0);
      void queryClient.invalidateQueries({ queryKey: ["urlCleanup"] });
    },
    onError: (err: unknown) => {
      toast.error(handleApiError(err).message);
    },
  });

  const toggleSelected = (id: number) => {
    const next = new Set(selectedIds);
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    setCustomSelection(next);
  };

  const selectAll = () => setCustomSelection(new Set(dirtyUrls.map((b) => b.audiobookId)));
  const clearSelection = () => setCustomSelection(new Set());

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <Button variant="ghost" size="sm" render={<Link to="/library" />}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </Button>
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <Link2 className="text-primary h-6 w-6" />
          Clean Book URLs
        </h1>
        <p className="text-muted-foreground text-sm">
          Find books whose saved website link carries tracking or session parameters (e.g. an
          Audible <code>ref=</code>/<code>pf_rd_*</code> tag) and strip them down to the clean,
          canonical URL. Newly fetched metadata is already saved clean — this is for links saved
          before that.
        </p>
      </div>

      <div className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-foreground text-lg font-bold">
            Books with Trackable URLs ({totalCount})
          </h2>
          {dirtyUrls.length > 0 && (
            <div className="flex flex-wrap items-center gap-2">
              <Button variant="link" size="sm" className="h-auto p-0 text-xs" onClick={selectAll}>
                Select all
              </Button>
              <Button
                variant="link"
                size="sm"
                className="h-auto p-0 text-xs"
                onClick={clearSelection}
              >
                Clear
              </Button>
              <Button
                size="sm"
                disabled={selectedIds.size === 0 || applyMutation.isPending}
                onClick={() => applyMutation.mutate(Array.from(selectedIds))}
              >
                {applyMutation.isPending ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <Sparkles className="mr-2 h-4 w-4" />
                )}
                Clean {selectedIds.size} URL{selectedIds.size === 1 ? "" : "s"}
              </Button>
            </div>
          )}
        </div>

        {isLoading ? (
          <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
            <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
            <p className="text-sm">Scanning saved URLs...</p>
          </div>
        ) : totalCount === 0 ? (
          <Card className="p-12 text-center">
            <Link2 className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
            <h3 className="text-foreground text-lg font-medium">No trackable URLs found</h3>
            <p className="text-muted-foreground mt-1 text-sm">
              Every saved book URL in your library is already clean.
            </p>
          </Card>
        ) : (
          <div className="space-y-2">
            {dirtyUrls.map((b) => (
              <div
                key={b.audiobookId}
                className="border-border bg-card flex items-start gap-3 rounded-lg border p-3"
              >
                <Checkbox
                  className="mt-1"
                  checked={selectedIds.has(b.audiobookId)}
                  onCheckedChange={() => toggleSelected(b.audiobookId)}
                  disabled={isFetching || applyMutation.isPending}
                />
                <div className="min-w-0 flex-1">
                  <Link
                    to="/library/book/$bookId"
                    params={{ bookId: String(b.audiobookId) }}
                    className="text-foreground font-semibold break-words hover:underline"
                  >
                    {b.authors.join(", ")} &mdash; {b.bookName}
                  </Link>
                  <div className="mt-1 space-y-0.5 text-xs">
                    <div className="text-muted-foreground break-all">
                      <span className="line-through decoration-red-500/60">{b.currentUrl}</span>
                    </div>
                    <div className="break-all text-emerald-600 dark:text-emerald-400">
                      {b.cleanedUrl}
                    </div>
                  </div>
                </div>
              </div>
            ))}

            {/* Stays rendered even if this page comes back empty while the count is non-zero
                (someone cleaned rows from another tab, or the clamp lagged a shrinking total),
                so the user can page back instead of staring at a dead-end heading. */}
            {pageCount > 1 && (
              <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
                <span className="text-muted-foreground text-xs">
                  Showing {currentPage * PAGE_SIZE + 1}–
                  {Math.min((currentPage + 1) * PAGE_SIZE, totalCount)} of {totalCount}
                </span>
                <div className="flex items-center gap-2">
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={currentPage === 0}
                    onClick={() => goToPage(currentPage - 1)}
                  >
                    Previous
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={currentPage >= pageCount - 1}
                    onClick={() => goToPage(currentPage + 1)}
                  >
                    Next
                  </Button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

export default CleanBookUrls;
