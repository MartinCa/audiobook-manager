import { useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { PAGE_SIZE } from "@/constants/paging";
import { SectionPager } from "./SectionPager";
import { browseApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import { formatDateTime } from "@/helpers/formatHelpers";
import type { AuthorConsistencyIssue } from "@/types/AuthorConsistencyIssue";

/**
 * The "authors whose last roster refresh failed" section of the /library/metadata-refresh page -
 * the author-scoped counterpart to SeriesConsistencyIssueList. A bulk or single author refresh
 * that throws leaves the failing author here (see
 * UpcomingReleaseService.RefreshAuthorRosterTrackedAsync) instead of only logging server-side, so
 * it can be retried individually. A successful refresh - from here or anywhere else - clears the
 * row.
 */
export function AuthorConsistencyIssueList() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(0);
  const [retryingAuthorId, setRetryingAuthorId] = useState<number | null>(null);

  const { data: pageData, isLoading } = useQuery({
    queryKey: queryKeys.authorConsistencyIssues.page(page),
    placeholderData: keepPreviousData,
    queryFn: () => browseApi.getAuthorConsistencyIssuesPage(page, PAGE_SIZE),
  });

  const retryMutation = useMutation({
    mutationFn: (personId: number) => browseApi.refreshAuthor(personId),
    onMutate: (personId) => setRetryingAuthorId(personId),
    onSuccess: () => {
      notifications.success("Author roster refreshed successfully");
      void queryClient.invalidateQueries({ queryKey: queryKeys.authorConsistencyIssues.all() });
    },
    onError: (err: unknown) => {
      // The failure already replaced this row's stored error
      // (RefreshAuthorRosterTrackedAsync), so refetching shows the fresh message without a
      // separate invalidate call here.
      notifications.error(handleApiError(err).message);
      void queryClient.invalidateQueries({ queryKey: queryKeys.authorConsistencyIssues.all() });
    },
    onSettled: () => setRetryingAuthorId(null),
  });

  const totalCount = pageData?.totalCount ?? 0;
  const items = (pageData?.items ?? []) as AuthorConsistencyIssue[];
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount - 1);

  if (!isLoading && totalCount === 0) {
    // No dedicated empty-state card, matching SeriesConsistencyIssueList: an empty list here is
    // the common case, and a full "nothing to see" card for every visit would be more noise than
    // a rarer empty state deserves.
    return null;
  }

  return (
    <div className="space-y-3">
      <h2 className="text-foreground text-lg font-bold">Author Refresh Failures ({totalCount})</h2>

      {isLoading ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-12">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Loading refresh failures...</p>
        </div>
      ) : (
        <div className="space-y-2">
          {items.map((item) => (
            <Card key={item.id} className="flex flex-col gap-2 p-3">
              <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-center">
                <div className="min-w-0 flex-1">
                  <div className="text-foreground font-semibold break-words">{item.authorName}</div>
                  <div className="text-muted-foreground mt-0.5 text-xs">
                    Failed {formatDateTime(item.detectedAt)}
                  </div>
                </div>
                <Button
                  size="sm"
                  variant="outline"
                  className="h-7 shrink-0 self-end text-xs sm:self-center"
                  disabled={retryMutation.isPending && retryingAuthorId === item.personId}
                  onClick={() => retryMutation.mutate(item.personId)}
                >
                  {retryMutation.isPending && retryingAuthorId === item.personId ? (
                    <Loader2 className="mr-1.5 h-3 w-3 animate-spin" />
                  ) : (
                    <RefreshCw className="mr-1.5 h-3 w-3" />
                  )}
                  Retry
                </Button>
              </div>
              <p className="text-destructive text-xs break-words">{item.errorMessage}</p>
            </Card>
          ))}

          {pageCount > 1 && (
            <SectionPager
              currentPage={currentPage}
              pageCount={pageCount}
              totalCount={totalCount}
              onPageChange={setPage}
            />
          )}
        </div>
      )}
    </div>
  );
}

export default AuthorConsistencyIssueList;
