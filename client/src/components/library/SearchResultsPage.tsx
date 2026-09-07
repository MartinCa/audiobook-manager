import { useState, useEffect } from "react";
import { Link, useNavigate } from "@tanstack/react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Search, X, Loader2, BookOpen, Users, BookMarked, Clock, ChevronRight } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { browseApi } from "@/services/api";
import { formatDuration } from "@/helpers/formatHelpers";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";
import type { AuthorSummary } from "@/types/AuthorSummary";
import type { LibrarySeriesHit } from "@/types/LibrarySearchResult";
import { Route } from "@/routes/library/search";

type SearchTab = "all" | "books" | "authors" | "series";

const PREVIEW_LIMIT = 5;
const PAGE_SIZE = 20;

function BookRow({ book }: { book: ManagedAudiobook }) {
  return (
    <Link
      key={book.id}
      to="/library/book/$bookId"
      params={{ bookId: String(book.id) }}
      className="group border-border bg-card hover:bg-muted/50 focus-visible:ring-ring flex cursor-pointer items-center justify-between rounded-lg border p-3 transition-colors focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none"
    >
      <div className="flex min-w-0 flex-1 items-center gap-3">
        <div className="bg-muted h-12 w-12 shrink-0 overflow-hidden rounded">
          {book.coverFilePath ? (
            <img
              src={browseApi.getCoverUrl(book.id)}
              alt={book.bookName ?? undefined}
              className="h-full w-full object-contain"
              onError={(e) => {
                (e.currentTarget as HTMLElement).style.display = "none";
              }}
            />
          ) : (
            <div className="text-muted-foreground flex h-full w-full items-center justify-center">
              <BookOpen className="h-6 w-6" />
            </div>
          )}
        </div>

        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-foreground max-w-full min-w-0 truncate font-semibold">
              {book.bookName}
            </span>
            {book.year && <span className="text-muted-foreground text-xs">({book.year})</span>}
          </div>

          <div className="text-muted-foreground flex flex-wrap items-center gap-x-2 text-xs">
            {book.authors && book.authors.length > 0 && (
              <span>By {book.authors.join(", ")} &middot;</span>
            )}
            {book.series && (
              <span>
                Series: {book.series} {book.seriesPart && `#${book.seriesPart}`} &middot;
              </span>
            )}
            {book.durationInSeconds != null && (
              <span className="flex items-center gap-1">
                <Clock className="h-3 w-3" />
                {formatDuration(book.durationInSeconds)}
              </span>
            )}
          </div>
        </div>
      </div>

      <ChevronRight className="text-muted-foreground group-hover:text-foreground h-4 w-4 shrink-0" />
    </Link>
  );
}

function AuthorRow({ author }: { author: AuthorSummary }) {
  return (
    <Link
      key={author.id}
      to="/library/authors/$authorId"
      params={{ authorId: String(author.id) }}
      className="group border-border bg-card hover:bg-muted/50 focus-visible:ring-ring flex cursor-pointer items-center justify-between rounded-lg border p-3 transition-colors focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none"
    >
      <div className="flex items-center gap-3">
        <Users className="text-primary h-4 w-4" />
        <div>
          <div className="text-foreground font-semibold">{author.name}</div>
          <div className="text-muted-foreground text-xs">
            {author.bookCount} {author.bookCount === 1 ? "book" : "books"}
          </div>
        </div>
      </div>

      <ChevronRight className="text-muted-foreground group-hover:text-foreground h-4 w-4" />
    </Link>
  );
}

function SeriesRow({ series }: { series: LibrarySeriesHit }) {
  return (
    <Link
      key={series.name}
      to="/library/series/$seriesName"
      params={{ seriesName: series.name }}
      className="group border-border bg-card hover:bg-muted/50 focus-visible:ring-ring flex cursor-pointer items-center justify-between rounded-lg border p-3 transition-colors focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none"
    >
      <div className="flex items-center gap-3">
        <BookMarked className="text-primary h-4 w-4" />
        <div>
          <div className="text-foreground font-semibold break-words">{series.name}</div>
          <div className="text-muted-foreground text-xs">
            {series.bookCount} {series.bookCount === 1 ? "book" : "books"}
          </div>
        </div>
      </div>

      <ChevronRight className="text-muted-foreground group-hover:text-foreground h-4 w-4" />
    </Link>
  );
}

function Pager({
  page,
  totalPages,
  onPageChange,
  disabled,
}: {
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  disabled: boolean;
}) {
  if (totalPages <= 1) return null;

  return (
    <div className="border-border flex items-center justify-between border-t pt-4">
      <div className="text-muted-foreground text-xs">
        Page {page} of {totalPages}
      </div>
      <div className="flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          disabled={page <= 1 || disabled}
          onClick={() => onPageChange(Math.max(1, page - 1))}
        >
          Previous
        </Button>
        <Button
          variant="outline"
          size="sm"
          disabled={page >= totalPages || disabled}
          onClick={() => onPageChange(Math.min(totalPages, page + 1))}
        >
          Next
        </Button>
      </div>
    </div>
  );
}

export function SearchResultsPage() {
  const navigate = useNavigate();
  const { q = "", tab = "all", page = 1 } = Route.useSearch();
  const [prevQ, setPrevQ] = useState(q);
  const [searchQuery, setSearchQuery] = useState(q);

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
          to: "/library/search",
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

  const handleClearSearch = () => {
    setSearchQuery("");
    if (q) {
      void navigate({
        to: "/library/search",
        search: (prev) => ({
          ...prev,
          q: undefined,
          page: undefined,
        }),
        replace: true,
      });
    }
  };

  const handleTabChange = (value: string | number) => {
    const nextTab = value as SearchTab;
    if (nextTab !== tab) {
      void navigate({
        to: "/library/search",
        search: (prev) => ({
          ...prev,
          tab: nextTab === "all" ? undefined : nextTab,
          page: undefined,
        }),
      });
    }
  };

  const handlePageChange = (newPage: number) => {
    void navigate({
      to: "/library/search",
      search: (prev) => ({
        ...prev,
        page: newPage > 1 ? newPage : undefined,
      }),
    });
  };

  const booksLimit = tab === "books" ? PAGE_SIZE : PREVIEW_LIMIT;
  const booksOffset = tab === "books" ? (page - 1) * PAGE_SIZE : 0;
  const authorsLimit = tab === "authors" ? PAGE_SIZE : PREVIEW_LIMIT;
  const authorsOffset = tab === "authors" ? (page - 1) * PAGE_SIZE : 0;
  const seriesLimit = tab === "series" ? PAGE_SIZE : PREVIEW_LIMIT;
  const seriesOffset = tab === "series" ? (page - 1) * PAGE_SIZE : 0;

  const booksQuery = useQuery({
    queryKey: ["searchResults", "books", q, tab === "books" ? page : 1, booksLimit, booksOffset],
    queryFn: () => browseApi.searchAudiobooks(q, booksLimit, booksOffset),
    enabled: Boolean(q),
    placeholderData: keepPreviousData,
  });

  const authorsQuery = useQuery({
    queryKey: [
      "searchResults",
      "authors",
      q,
      tab === "authors" ? page : 1,
      authorsLimit,
      authorsOffset,
    ],
    queryFn: () => browseApi.searchAuthors(q, authorsLimit, authorsOffset),
    enabled: Boolean(q),
    placeholderData: keepPreviousData,
  });

  const seriesQuery = useQuery({
    queryKey: [
      "searchResults",
      "series",
      q,
      tab === "series" ? page : 1,
      seriesLimit,
      seriesOffset,
    ],
    queryFn: () => browseApi.searchSeries(q, seriesLimit, seriesOffset),
    enabled: Boolean(q),
    placeholderData: keepPreviousData,
  });

  const books = booksQuery.data?.items ?? [];
  const booksTotal = booksQuery.data?.total ?? 0;
  const authors = authorsQuery.data?.items ?? [];
  const authorsTotal = authorsQuery.data?.total ?? 0;
  const series = seriesQuery.data?.items ?? [];
  const seriesTotal = seriesQuery.data?.total ?? 0;

  const isLoading = booksQuery.isLoading || authorsQuery.isLoading || seriesQuery.isLoading;

  const booksTotalPages = Math.ceil(booksTotal / PAGE_SIZE) || 1;
  const authorsTotalPages = Math.ceil(authorsTotal / PAGE_SIZE) || 1;
  const seriesTotalPages = Math.ceil(seriesTotal / PAGE_SIZE) || 1;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
            <Search className="text-primary h-6 w-6" />
            Search
          </h1>
          <p className="text-muted-foreground text-sm">
            Search books, authors, and series across your library.
          </p>
        </div>
      </div>

      <div className="relative max-w-md">
        <Search className="text-muted-foreground absolute top-2.5 left-3 h-4 w-4" />
        <Input
          placeholder="Search title, author, series, description..."
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              const trimmed = searchQuery.trim();
              if (trimmed !== q) {
                void navigate({
                  to: "/library/search",
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

      {!q ? (
        <Card className="p-12 text-center">
          <Search className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
          <h3 className="text-foreground text-lg font-medium">Type a query to search</h3>
          <p className="text-muted-foreground mt-1 text-sm">
            Search across book titles, subtitles, series, authors, and descriptions.
          </p>
        </Card>
      ) : isLoading && !booksQuery.data && !authorsQuery.data && !seriesQuery.data ? (
        <div className="text-muted-foreground flex flex-col items-center justify-center py-20">
          <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
          <p className="text-sm">Searching...</p>
        </div>
      ) : (
        <>
          <Tabs value={tab} onValueChange={handleTabChange}>
            <TabsList className="h-9">
              <TabsTrigger value="all" className="text-xs">
                All
              </TabsTrigger>
              <TabsTrigger value="books" className="text-xs">
                <BookOpen className="mr-1.5 h-3.5 w-3.5" />
                Books ({booksTotal})
              </TabsTrigger>
              <TabsTrigger value="authors" className="text-xs">
                <Users className="mr-1.5 h-3.5 w-3.5" />
                Authors ({authorsTotal})
              </TabsTrigger>
              <TabsTrigger value="series" className="text-xs">
                <BookMarked className="mr-1.5 h-3.5 w-3.5" />
                Series ({seriesTotal})
              </TabsTrigger>
            </TabsList>
          </Tabs>

          {tab === "all" && (
            <div className="space-y-8">
              <section className="space-y-2">
                <div className="flex items-center justify-between">
                  <h2 className="text-foreground text-lg font-bold">Books ({booksTotal})</h2>
                  {booksTotal > PREVIEW_LIMIT && (
                    <Button
                      variant="link"
                      size="sm"
                      className="h-auto p-0 text-xs"
                      onClick={() => handleTabChange("books")}
                    >
                      View all {booksTotal} books &rarr;
                    </Button>
                  )}
                </div>
                {books.length === 0 ? (
                  <p className="text-muted-foreground text-sm">No books matched your query.</p>
                ) : (
                  <div className="space-y-2">
                    {books.map((book) => (
                      <BookRow key={book.id} book={book} />
                    ))}
                  </div>
                )}
              </section>

              <section className="space-y-2">
                <div className="flex items-center justify-between">
                  <h2 className="text-foreground text-lg font-bold">Authors ({authorsTotal})</h2>
                  {authorsTotal > PREVIEW_LIMIT && (
                    <Button
                      variant="link"
                      size="sm"
                      className="h-auto p-0 text-xs"
                      onClick={() => handleTabChange("authors")}
                    >
                      View all {authorsTotal} authors &rarr;
                    </Button>
                  )}
                </div>
                {authors.length === 0 ? (
                  <p className="text-muted-foreground text-sm">No authors matched your query.</p>
                ) : (
                  <div className="space-y-2">
                    {authors.map((author) => (
                      <AuthorRow key={author.id} author={author} />
                    ))}
                  </div>
                )}
              </section>

              <section className="space-y-2">
                <div className="flex items-center justify-between">
                  <h2 className="text-foreground text-lg font-bold">Series ({seriesTotal})</h2>
                  {seriesTotal > PREVIEW_LIMIT && (
                    <Button
                      variant="link"
                      size="sm"
                      className="h-auto p-0 text-xs"
                      onClick={() => handleTabChange("series")}
                    >
                      View all {seriesTotal} series &rarr;
                    </Button>
                  )}
                </div>
                {series.length === 0 ? (
                  <p className="text-muted-foreground text-sm">No series matched your query.</p>
                ) : (
                  <div className="space-y-2">
                    {series.map((s) => (
                      <SeriesRow key={s.name} series={s} />
                    ))}
                  </div>
                )}
              </section>
            </div>
          )}

          {tab === "books" &&
            (books.length === 0 ? (
              <Card className="p-12 text-center">
                <BookOpen className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
                <h3 className="text-foreground text-lg font-medium">No books found</h3>
                <p className="text-muted-foreground mt-1 text-sm">No books matched your query.</p>
              </Card>
            ) : (
              <div className="space-y-2">
                {books.map((book) => (
                  <BookRow key={book.id} book={book} />
                ))}
                <Pager
                  page={page}
                  totalPages={booksTotalPages}
                  onPageChange={handlePageChange}
                  disabled={booksQuery.isFetching}
                />
              </div>
            ))}

          {tab === "authors" &&
            (authors.length === 0 ? (
              <Card className="p-12 text-center">
                <Users className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
                <h3 className="text-foreground text-lg font-medium">No authors found</h3>
                <p className="text-muted-foreground mt-1 text-sm">No authors matched your query.</p>
              </Card>
            ) : (
              <div className="space-y-2">
                {authors.map((author) => (
                  <AuthorRow key={author.id} author={author} />
                ))}
                <Pager
                  page={page}
                  totalPages={authorsTotalPages}
                  onPageChange={handlePageChange}
                  disabled={authorsQuery.isFetching}
                />
              </div>
            ))}

          {tab === "series" &&
            (series.length === 0 ? (
              <Card className="p-12 text-center">
                <BookMarked className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
                <h3 className="text-foreground text-lg font-medium">No series found</h3>
                <p className="text-muted-foreground mt-1 text-sm">No series matched your query.</p>
              </Card>
            ) : (
              <div className="space-y-2">
                {series.map((s) => (
                  <SeriesRow key={s.name} series={s} />
                ))}
                <Pager
                  page={page}
                  totalPages={seriesTotalPages}
                  onPageChange={handlePageChange}
                  disabled={seriesQuery.isFetching}
                />
              </div>
            ))}
        </>
      )}
    </div>
  );
}

export default SearchResultsPage;
