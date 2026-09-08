import { useState, useEffect } from "react";
import { Link, useNavigate } from "@tanstack/react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Users, Search, X, ChevronRight, Loader2, BookOpen } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { LibraryViewTabs } from "./LibraryViewTabs";
import { browseApi } from "@/services/api";
import { useClampedPage } from "@/hooks/useClampedPage";
import { Route } from "@/routes/library/authors/index";

const PAGE_SIZE = 50;

export function AuthorsList() {
  const navigate = useNavigate();
  const { q = "" } = Route.useSearch();
  const [prevQ, setPrevQ] = useState(q);
  const [filter, setFilter] = useState(q);
  const [page, setPage] = useState(0);

  if (prevQ !== q) {
    setPrevQ(q);
    if (filter.trim() !== q) {
      setFilter(q);
    }
  }

  useEffect(() => {
    const timer = setTimeout(() => {
      const trimmed = filter.trim();
      if (trimmed !== q) {
        setPage(0);
        void navigate({
          to: "/library/authors",
          search: (prev) => ({
            ...prev,
            q: trimmed || undefined,
          }),
          replace: true,
        });
      }
    }, 300);
    return () => clearTimeout(timer);
  }, [filter, q, navigate]);

  const handleClearFilter = () => {
    setFilter("");
    setPage(0);
    if (q) {
      void navigate({
        to: "/library/authors",
        search: (prev) => ({
          ...prev,
          q: undefined,
        }),
        replace: true,
      });
    }
  };

  // The list is paged server-side: the filter also runs in SQL (accent-insensitive), so only the
  // requested page crosses the wire - the old version sent every author in the library.
  const { data: pageData, isLoading: loading } = useQuery({
    queryKey: ["authors", q, page],
    placeholderData: keepPreviousData,
    queryFn: () => browseApi.getAuthorPage(PAGE_SIZE, page * PAGE_SIZE, q),
  });

  const authors = pageData?.items ?? [];
  const totalCount = pageData?.total ?? 0;
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount - 1);

  // A filter change or an external shrink can leave the raw page out of range; pull it back so
  // the next fetch lands on a valid page rather than coming back empty (see useClampedPage).
  useClampedPage(page, pageCount, setPage);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <LibraryViewTabs activeTab="authors" />
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <Users className="text-primary h-6 w-6" />
          Authors ({totalCount})
        </h1>
        <p className="text-muted-foreground text-sm">Browse books and series grouped by author.</p>
      </div>

      <div className="relative max-w-md">
        <Search className="text-muted-foreground absolute top-2.5 left-3 h-4 w-4" />
        <Input
          placeholder="Filter authors..."
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              const trimmed = filter.trim();
              if (trimmed !== q) {
                setPage(0);
                void navigate({
                  to: "/library/authors",
                  search: (prev) => ({
                    ...prev,
                    q: trimmed || undefined,
                  }),
                  replace: true,
                });
              }
            }
          }}
          className="pr-9 pl-9"
        />
        {filter ? (
          <button
            type="button"
            onClick={handleClearFilter}
            aria-label="Clear filter"
            className="text-muted-foreground hover:text-foreground absolute top-2.5 right-2.5 cursor-pointer rounded-sm p-0.5 transition-colors"
          >
            <X className="h-4 w-4" />
          </button>
        ) : null}
      </div>

      {loading && authors.length === 0 ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Loading authors...</p>
        </div>
      ) : totalCount === 0 ? (
        <Card className="p-12 text-center">
          <Users className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
          <h3 className="text-foreground text-lg font-medium">No authors found</h3>
          <p className="text-muted-foreground mt-1 text-sm">
            {q.trim()
              ? "No authors match your search filter."
              : "No authors tracked in the library."}
          </p>
        </Card>
      ) : (
        <div className="space-y-2">
          {authors.map((author) => (
            <Link
              key={author.id}
              to="/library/authors/$authorId"
              params={{ authorId: String(author.id) }}
              className="group border-border bg-card hover:bg-muted/50 focus-visible:ring-ring flex cursor-pointer items-center justify-between rounded-lg border p-3 transition-colors focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none"
            >
              <div className="flex items-center gap-3">
                <BookOpen className="text-primary h-4 w-4" />
                <div>
                  <div className="text-foreground font-semibold">{author.name}</div>
                  <div className="text-muted-foreground text-xs">
                    {author.bookCount} {author.bookCount === 1 ? "book" : "books"}
                  </div>
                </div>
              </div>

              <ChevronRight className="text-muted-foreground group-hover:text-foreground h-4 w-4" />
            </Link>
          ))}

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
                  onClick={() => setPage(currentPage - 1)}
                >
                  Previous
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={currentPage >= pageCount - 1}
                  onClick={() => setPage(currentPage + 1)}
                >
                  Next
                </Button>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default AuthorsList;
