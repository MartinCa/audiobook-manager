import { useState, useEffect } from "react";
import { Link, useNavigate } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import {
  Library,
  BookMarked,
  Users,
  Search,
  RefreshCw,
  Clock,
  AlertTriangle,
  ChevronRight,
  FolderSearch,
  Layers,
  Tag,
  ShieldAlert,
  Loader2,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { browseApi, consistencyApi } from "@/services/api";
import { formatDuration } from "@/helpers/formatHelpers";

export function BookLibrary() {
  const navigate = useNavigate();
  const [searchQuery, setSearchQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [page, setPage] = useState(1);
  const pageSize = 20;

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedQuery(searchQuery);
      setPage(1);
    }, 300);
    return () => clearTimeout(timer);
  }, [searchQuery]);

  const offset = (page - 1) * pageSize;
  const {
    data,
    isLoading: loading,
    refetch,
  } = useQuery({
    queryKey: ["books", debouncedQuery, page, pageSize],
    queryFn: async () => {
      const [browseRes, issuesRes] = await Promise.all([
        debouncedQuery.trim()
          ? browseApi.searchAudiobooks(debouncedQuery.trim(), pageSize, offset)
          : browseApi.getAudiobooks(pageSize, offset),
        consistencyApi.getIssues().catch(() => []),
      ]);

      const counts: Record<number, number> = {};
      for (const issue of issuesRes) {
        counts[issue.audiobookId] = (counts[issue.audiobookId] || 0) + 1;
      }

      return {
        books: browseRes.items,
        totalCount: browseRes.total,
        issueSummary: counts,
      };
    },
  });

  const books = data?.books ?? [];
  const totalCount = data?.totalCount ?? 0;
  const issueSummary = data?.issueSummary ?? {};
  const totalPages = Math.ceil(totalCount / pageSize) || 1;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
            <Library className="text-primary h-6 w-6" />
            Library Audiobooks
          </h1>
          <p className="text-muted-foreground text-sm">
            Browse and manage organized audiobooks in your collection.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button variant="outline" size="sm" render={<Link to="/library/series" />}>
            <BookMarked className="mr-1.5 h-4 w-4" />
            Series
          </Button>

          <Button variant="outline" size="sm" render={<Link to="/library/authors" />}>
            <Users className="mr-1.5 h-4 w-4" />
            Authors
          </Button>

          <Button variant="outline" size="sm" render={<Link to="/library/discovered" />}>
            <FolderSearch className="mr-1.5 h-4 w-4" />
            Discovered Files
          </Button>

          <Button variant="outline" size="sm" render={<Link to="/library/consistency" />}>
            <ShieldAlert className="mr-1.5 h-4 w-4" />
            Consistency
          </Button>

          <Button variant="outline" size="sm" render={<Link to="/library/missing-tags" />}>
            <Tag className="mr-1.5 h-4 w-4" />
            Missing Tags
          </Button>

          <Button variant="outline" size="sm" render={<Link to="/library/similar-values" />}>
            <Layers className="mr-1.5 h-4 w-4" />
            Similar Values
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              void refetch();
            }}
            disabled={loading}
          >
            <RefreshCw className={`mr-1.5 h-4 w-4 ${loading ? "animate-spin" : ""}`} />
            Reload
          </Button>
        </div>
      </div>

      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative max-w-md flex-1">
          <Search className="text-muted-foreground absolute top-2.5 left-3 h-4 w-4" />
          <Input
            placeholder="Search title, author, series, narrator..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="pl-9"
          />
        </div>

        <div className="text-muted-foreground text-xs">
          Showing {books.length} of {totalCount} audiobooks
        </div>
      </div>

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
            {debouncedQuery
              ? "No audiobooks matched your query."
              : "No audiobooks have been organized yet. Check your organize queue or import discovered files."}
          </p>
        </Card>
      ) : (
        <div className="space-y-2">
          {books.map((book) => {
            const issueCount = issueSummary[book.id] ?? 0;
            return (
              <div
                key={book.id}
                onClick={() => {
                  void navigate({
                    to: "/library/book/$bookId",
                    params: { bookId: String(book.id) },
                  });
                }}
                className="group border-border bg-card hover:bg-muted/50 flex cursor-pointer items-center justify-between rounded-lg border p-3 transition-colors"
              >
                <div className="flex min-w-0 items-center gap-3">
                  <div className="bg-muted h-12 w-12 shrink-0 overflow-hidden rounded">
                    {book.coverFilePath ? (
                      <img
                        src={browseApi.getCoverUrl(book.id)}
                        alt={book.bookName ?? undefined}
                        className="h-full w-full object-cover"
                        onError={(e) => {
                          (e.currentTarget as HTMLElement).style.display = "none";
                        }}
                      />
                    ) : (
                      <div className="text-muted-foreground flex h-full w-full items-center justify-center">
                        <Library className="h-6 w-6" />
                      </div>
                    )}
                  </div>

                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-foreground truncate font-semibold">
                        {book.bookName}
                      </span>
                      {book.year && (
                        <span className="text-muted-foreground text-xs">({book.year})</span>
                      )}
                      {issueCount > 0 && (
                        <Badge variant="destructive" className="h-5 gap-1 px-1.5 text-[10px]">
                          <AlertTriangle className="h-2.5 w-2.5" />
                          {issueCount} {issueCount === 1 ? "issue" : "issues"}
                        </Badge>
                      )}
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
                      {book.narrators && book.narrators.length > 0 && (
                        <span>Narrated by {book.narrators.join(", ")} &middot;</span>
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
              </div>
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
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={page >= totalPages || loading}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
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
