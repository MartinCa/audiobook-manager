import { useState, useEffect } from "react";
import { useNavigate } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { Library, Search, X, RefreshCw, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { BookListRow } from "./library/BookListRow";
import { BookBulkActionBar } from "./library/BookBulkActionBar";
import { LibraryViewTabs } from "./library/LibraryViewTabs";
import { browseApi, consistencyApi, metadataRefreshApi } from "@/services/api";
import { useBookSelection } from "@/hooks/useBookSelection";
import { Route } from "@/routes/library/index";

/** Typed so a failed summary fetch still indexes as a count map rather than widening to {}. */
const NO_ISSUE_COUNTS: Record<number, number> = {};

/** Pending-metadata ids only gain members through a refresh; an empty set is the safe fallback. */
const NO_PENDING_IDS: number[] = [];

export function BookLibrary() {
  const navigate = useNavigate();
  const selection = useBookSelection();
  const { q = "", page = 1 } = Route.useSearch();
  const [prevQ, setPrevQ] = useState(q);
  const [searchQuery, setSearchQuery] = useState(q);
  const pageSize = 20;

  if (prevQ !== q) {
    setPrevQ(q);
    if (searchQuery.trim() !== q) {
      setSearchQuery(q);
    }
  }

  useEffect(() => {
    const timer = setTimeout(() => {
      const trimmed = searchQuery.trim();
      if (trimmed !== q) {
        void navigate({
          to: "/library",
          search: (prev) => ({
            ...prev,
            q: trimmed || undefined,
            page: undefined,
          }),
          replace: true,
        });
      }
    }, 300);
    return () => clearTimeout(timer);
  }, [searchQuery, q, navigate]);

  const {
    data,
    isLoading: loading,
    refetch,
  } = useQuery({
    queryKey: ["books", q, page, pageSize],
    queryFn: async () => {
      const offset = (page - 1) * pageSize;
      const [browseRes, issuesRes, pendingIds] = await Promise.all([
        q.trim()
          ? browseApi.searchAudiobooks(q.trim(), pageSize, offset)
          : browseApi.getAudiobooks(pageSize, offset),
        // The summary endpoint, which counts per audiobook in the database. This used to fetch
        // every issue and count them here - the whole table, including the metadata.opf and
        // description bodies stored on each row, to render a badge number per book.
        consistencyApi.getIssueSummary().catch(() => NO_ISSUE_COUNTS),
        // Sparse id list of books with a pending metadata-refresh snapshot, for a soft badge.
        metadataRefreshApi.getPendingSummary().catch(() => NO_PENDING_IDS),
      ]);

      return {
        books: browseRes.items,
        totalCount: browseRes.total,
        issueSummary: issuesRes,
        pendingRefreshIds: new Set(pendingIds),
      };
    },
  });

  const handlePageChange = (newPage: number) => {
    void navigate({
      to: "/library",
      search: (prev) => ({
        ...prev,
        page: newPage > 1 ? newPage : undefined,
      }),
    });
  };

  const handleClearSearch = () => {
    setSearchQuery("");
    if (q) {
      void navigate({
        to: "/library",
        search: (prev) => ({
          ...prev,
          q: undefined,
          page: undefined,
        }),
        replace: true,
      });
    }
  };

  const books = data?.books ?? [];
  const totalCount = data?.totalCount ?? 0;
  const issueSummary = data?.issueSummary ?? {};
  const pendingRefreshIds = data?.pendingRefreshIds ?? new Set<number>();
  const totalPages = Math.ceil(totalCount / pageSize) || 1;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <LibraryViewTabs activeTab="books" />

        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              void refetch();
            }}
            disabled={loading}
          >
            <RefreshCw className={`mr-1.5 h-3.5 w-3.5 ${loading ? "animate-spin" : ""}`} />
            Reload
          </Button>
        </div>
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <Library className="text-primary h-6 w-6" />
          Library Audiobooks
        </h1>
        <p className="text-muted-foreground text-sm">
          Browse and manage organized audiobooks in your collection.
        </p>
      </div>

      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative max-w-md flex-1">
          <Search className="text-muted-foreground absolute top-2.5 left-3 h-4 w-4" />
          <Input
            placeholder="Search title, author, series, narrator..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                const trimmed = searchQuery.trim();
                if (trimmed !== q) {
                  void navigate({
                    to: "/library",
                    search: (prev) => ({
                      ...prev,
                      q: trimmed || undefined,
                      page: undefined,
                    }),
                    replace: true,
                  });
                }
              }
            }}
            className="pr-9 pl-9"
          />
          {searchQuery ? (
            <button
              type="button"
              onClick={handleClearSearch}
              aria-label="Clear search"
              className="text-muted-foreground hover:text-foreground absolute top-2.5 right-2.5 cursor-pointer rounded-sm p-0.5 transition-colors"
            >
              <X className="h-4 w-4" />
            </button>
          ) : null}
        </div>

        <div className="flex items-center gap-3">
          <Checkbox
            id="select-page"
            disabled={books.length === 0}
            checked={books.length > 0 && selection.pageAllSelected(books)}
            indeterminate={books.length > 0 && selection.pageSomeSelected(books)}
            onCheckedChange={(checked) => {
              if (checked) {
                selection.selectPage(books);
              } else {
                selection.deselectPage(books);
              }
            }}
          />
          <label
            htmlFor="select-page"
            className="text-muted-foreground cursor-pointer text-xs leading-none select-none"
          >
            Select page
          </label>
          <div className="text-muted-foreground text-xs">
            Showing {books.length} of {totalCount} audiobooks
          </div>
        </div>
      </div>

      <BookBulkActionBar selection={selection} />

      {loading && books.length === 0 ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-20">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Loading library audiobooks...</p>
        </div>
      ) : books.length === 0 ? (
        <Card className="p-12 text-center">
          <Library className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
          <h3 className="text-foreground text-lg font-medium">No audiobooks found</h3>
          <p className="text-muted-foreground mt-1 text-sm">
            {q
              ? "No audiobooks matched your query."
              : "No audiobooks have been organized yet. Check your organize queue or import discovered files."}
          </p>
        </Card>
      ) : (
        <div className="space-y-2">
          {books.map((book) => {
            const issueCount = issueSummary[book.id] ?? 0;
            const hasPendingRefresh = pendingRefreshIds.has(book.id);
            return (
              <BookListRow
                key={book.id}
                book={book}
                issueCount={issueCount}
                hasPendingRefresh={hasPendingRefresh}
                selectable
                selected={selection.isSelected(book.id)}
                onSelectedChange={() => selection.toggle(book)}
              />
            );
          })}
        </div>
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
              onClick={() => handlePageChange(Math.max(1, page - 1))}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={page >= totalPages || loading}
              onClick={() => handlePageChange(Math.min(totalPages, page + 1))}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

export default BookLibrary;
