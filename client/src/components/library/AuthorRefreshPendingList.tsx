import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowRight, CheckCircle2, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import type { PageSizeOption } from "@/constants/paging";
import { LastRefreshedHint } from "@/components/LastRefreshedHint";
import { RenameValueDialog } from "@/components/RenameValueDialog";
import { SectionPager } from "./SectionPager";
import { browseApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { usePageSize } from "@/hooks/usePageSize";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type { AuthorRefreshPending } from "@/types/AuthorRefreshPending";

/**
 * The "authors the source names differently" section of the /library/metadata-refresh page: one
 * page of the server-side paged pending list (rows exist only for authors whose last roster
 * refresh found the source spelling the name differently from the library - after both follow the
 * library's initials convention), each row offering to rename the author and every book to the
 * source's spelling, or to dismiss it. The count comes from a cheap count endpoint, never from
 * fetching the full list.
 */
export function AuthorRefreshPendingList() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePageSize();
  const [reviewing, setReviewing] = useState<AuthorRefreshPending | null>(null);
  const [dismissingId, setDismissingId] = useState<number | null>(null);

  const { data: pageData, isLoading } = useQuery({
    queryKey: queryKeys.authorPending.page(page, pageSize),
    placeholderData: keepPreviousData,
    queryFn: () => browseApi.getAuthorPendingRefreshPage(page, pageSize),
  });

  const { data: totalCount } = useQuery({
    queryKey: queryKeys.authorPending.count(),
    queryFn: () => browseApi.getAuthorPendingRefreshCount(),
  });

  // Clamped here rather than only where the pager is drawn, so the page that is *fetched* and the
  // page that is *displayed* can never disagree.
  const count = totalCount ?? pageData?.totalCount ?? 0;
  const items = pageData?.items ?? [];
  const pageCount = Math.max(1, Math.ceil(count / pageSize));
  const currentPage = Math.min(page, pageCount - 1);

  const handlePageSizeChange = (size: PageSizeOption) => {
    setPageSize(size);
    setPage(0);
  };

  const handleDismiss = async (item: AuthorRefreshPending) => {
    setDismissingId(item.authorId);
    try {
      await browseApi.dismissAuthorPendingRefresh(item.authorId);
      notifications.success(`Dismissed the proposed name for ${item.authorName}`);
      void queryClient.invalidateQueries({ queryKey: queryKeys.authorPending.all() });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setDismissingId(null);
    }
  };

  return (
    <div className="space-y-3">
      <h2 className="text-foreground text-lg font-bold">
        Authors with a Different Name at the Source ({count})
      </h2>

      {isLoading ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-12">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Loading proposed author names...</p>
        </div>
      ) : count === 0 ? (
        <Card className="p-10 text-center">
          <CheckCircle2 className="text-muted-foreground/40 mx-auto mb-3 h-10 w-10" />
          <h3 className="text-foreground text-base font-medium">No author names to review</h3>
          <p className="text-muted-foreground mt-1 text-sm">
            Refreshing a matched author whose source spells their name differently from your library
            stores the proposed name here. Differences that are only initials spacing or punctuation
            are ignored, following your library's initials setting.
          </p>
        </Card>
      ) : (
        <div className="space-y-2">
          {items.map((item) => (
            <div
              key={item.authorId}
              className="border-border bg-card hover:bg-muted/50 flex flex-col justify-between gap-3 rounded-lg border p-3 transition-colors sm:flex-row sm:items-center"
            >
              <div className="min-w-0 flex-1">
                <div className="text-foreground flex flex-wrap items-center gap-x-2 gap-y-1 font-semibold break-words">
                  <Link
                    to="/library/authors/$authorId"
                    params={{ authorId: String(item.authorId) }}
                    className="hover:underline"
                  >
                    {item.authorName}
                  </Link>
                  <ArrowRight className="text-muted-foreground h-3.5 w-3.5 shrink-0" />
                  <span>{item.proposedName}</span>
                </div>
                <div className="text-muted-foreground mt-0.5 text-xs">
                  <LastRefreshedHint lastRefreshedAt={item.fetchedAt} />
                </div>
              </div>
              <div className="flex shrink-0 items-center gap-2 self-end sm:self-center">
                <Badge variant="secondary" className="shrink-0">
                  {item.sourceName}
                </Badge>
                <Button
                  variant="outline"
                  size="sm"
                  className="h-7 text-xs"
                  disabled={dismissingId === item.authorId}
                  onClick={() => {
                    void handleDismiss(item);
                  }}
                >
                  Dismiss
                </Button>
                <Button size="sm" className="h-7 text-xs" onClick={() => setReviewing(item)}>
                  Review
                </Button>
              </div>
            </div>
          ))}

          {/* Stays rendered even if this page comes back empty while the count is non-zero, so
              the user can page back instead of staring at a dead-end heading. */}
          <SectionPager
            currentPage={currentPage}
            pageCount={pageCount}
            totalCount={count}
            pageSize={pageSize}
            onPageChange={setPage}
            onPageSizeChange={handlePageSizeChange}
          />
        </div>
      )}

      {reviewing ? (
        <RenameValueDialog
          open
          onOpenChange={(open) => {
            if (!open) setReviewing(null);
          }}
          valueType="author"
          currentName={reviewing.authorName}
          initialNewName={reviewing.proposedName}
          onRenamed={() => {
            setReviewing(null);
            // A rename rewrites every book of the author, so far more than the pending list is stale.
            void queryClient.invalidateQueries();
          }}
        />
      ) : null}
    </div>
  );
}

export default AuthorRefreshPendingList;
