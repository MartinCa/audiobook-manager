import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { Library, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { OwnedBookList } from "./library/OwnedBookList";
import { LibraryViewTabs } from "./library/LibraryViewTabs";
import { browseApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useBookSelection } from "@/hooks/useBookSelection";
import { Route } from "@/routes/library/index";
import type { BookListFilters } from "@/types/EntityFilters";

const PAGE_SIZE = 20;

export function BookLibrary() {
  const navigate = useNavigate();
  const selection = useBookSelection();
  const { q = "", page = 1, ...filterSearch } = Route.useSearch();
  const filters: BookListFilters = filterSearch;

  const handleFiltersChange = (next: BookListFilters) => {
    void navigate({
      to: "/library",
      search: (prev) => ({ ...prev, ...next, page: undefined }),
      replace: true,
    });
  };

  const handleSearchChange = (next: string) => {
    void navigate({
      to: "/library",
      search: (prev) => ({ ...prev, q: next || undefined, page: undefined }),
      replace: true,
    });
  };

  const {
    data,
    isLoading: loading,
    refetch,
  } = useQuery({
    queryKey: queryKeys.books.page(q, page, PAGE_SIZE, filters),
    queryFn: async () => {
      const offset = (page - 1) * PAGE_SIZE;
      const browseRes = q.trim()
        ? await browseApi.searchAudiobooks(q.trim(), PAGE_SIZE, offset, filters)
        : await browseApi.getAudiobooks(PAGE_SIZE, offset, filters);
      return { books: browseRes.items, totalCount: browseRes.total };
    },
  });

  const handlePageChange = (newPage0Indexed: number) => {
    const newPage = newPage0Indexed + 1;
    void navigate({
      to: "/library",
      search: (prev) => ({
        ...prev,
        page: newPage > 1 ? newPage : undefined,
      }),
    });
  };

  const books = data?.books ?? [];
  const totalCount = data?.totalCount ?? 0;
  const pageCount = Math.ceil(totalCount / PAGE_SIZE) || 1;

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

      <OwnedBookList
        books={books}
        totalCount={totalCount}
        loading={loading}
        loadingLabel="Loading library audiobooks..."
        emptyState={
          <Card className="p-12 text-center">
            <Library className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
            <h3 className="text-foreground text-lg font-medium">No audiobooks found</h3>
            <p className="text-muted-foreground mt-1 text-sm">
              {q
                ? "No audiobooks matched your query."
                : "No audiobooks have been organized yet. Check your organize queue or import discovered files."}
            </p>
          </Card>
        }
        selection={selection}
        search={q}
        onSearchChange={handleSearchChange}
        filters={filters}
        onFiltersChange={handleFiltersChange}
        page={page - 1}
        pageCount={pageCount}
        pageSize={PAGE_SIZE}
        pagerDisabled={loading}
        onPageChange={handlePageChange}
      />
    </div>
  );
}

export default BookLibrary;
