import { useState, useEffect, useRef } from "react";
import { Link, useNavigate } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { Search, X, BookOpen, Users, BookMarked, Loader2 } from "lucide-react";
import { Input } from "@/components/ui/input";
import { browseApi } from "@/services/api";

export function LibrarySearch() {
  const navigate = useNavigate();
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [focused, setFocused] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedQuery(query.trim());
    }, 250);
    return () => clearTimeout(timer);
  }, [query]);

  const { data: results = null, isLoading: loading } = useQuery({
    queryKey: ["quickSearch", debouncedQuery],
    queryFn: () => browseApi.searchLibrary(debouncedQuery, 5),
    enabled: Boolean(debouncedQuery),
  });

  // Click outside to close dropdown
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setFocused(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const handleSelect = () => {
    setFocused(false);
    setQuery("");
  };

  const goToSearchResults = (searchTerm: string) => {
    const trimmed = searchTerm.trim();
    if (!trimmed) return;
    void navigate({ to: "/library/search", search: { q: trimmed } });
    handleSelect();
  };

  const hasResults =
    results &&
    (results.books.length > 0 || results.authors.length > 0 || results.series.length > 0);

  const isOpen = focused && Boolean(debouncedQuery) && Boolean(hasResults);

  return (
    <div ref={containerRef} className="relative w-full max-w-sm">
      <div className="relative">
        <Search className="text-muted-foreground absolute top-2.5 left-2.5 h-4 w-4" />
        <Input
          placeholder="Quick search books, authors, series..."
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
            setFocused(true);
          }}
          onFocus={() => setFocused(true)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              goToSearchResults(query);
            }
          }}
          className="h-9 pr-8 pl-8 text-xs"
        />
        {loading ? (
          <Loader2 className="text-muted-foreground absolute top-2.5 right-2.5 h-4 w-4 animate-spin" />
        ) : query ? (
          <button
            type="button"
            onClick={() => {
              setQuery("");
              setFocused(false);
            }}
            className="text-muted-foreground hover:text-foreground absolute top-2.5 right-2.5"
          >
            <X className="h-4 w-4" />
          </button>
        ) : null}
      </div>

      {isOpen && results && (
        <div className="border-border bg-popover text-popover-foreground absolute top-full right-0 left-0 z-50 mt-1 max-h-96 overflow-y-auto rounded-md border p-2 shadow-lg">
          {results.books.length > 0 && (
            <div className="mb-2">
              <div className="text-muted-foreground px-2 py-1 text-[11px] font-semibold uppercase">
                Books
              </div>
              {results.books.map((b) => (
                <Link
                  key={`book-${b.id}`}
                  to="/library/book/$bookId"
                  params={{ bookId: String(b.id) }}
                  onClick={handleSelect}
                  className="hover:bg-accent focus-visible:ring-ring flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 text-xs transition-colors focus-visible:ring-2 focus-visible:outline-none"
                >
                  <BookOpen className="text-primary h-3.5 w-3.5 shrink-0" />
                  <div className="truncate">
                    <span className="text-foreground font-medium">{b.bookName}</span>
                    {b.authors.length > 0 && (
                      <span className="text-muted-foreground">
                        {" "}
                        &middot; {b.authors.join(", ")}
                      </span>
                    )}
                  </div>
                </Link>
              ))}
            </div>
          )}

          {results.authors.length > 0 && (
            <div className="mb-2">
              <div className="text-muted-foreground px-2 py-1 text-[11px] font-semibold uppercase">
                Authors
              </div>
              {results.authors.map((a) => (
                <Link
                  key={`author-${a.id}`}
                  to="/library/authors/$authorId"
                  params={{ authorId: String(a.id) }}
                  onClick={handleSelect}
                  className="hover:bg-accent focus-visible:ring-ring flex cursor-pointer items-center justify-between gap-2 rounded px-2 py-1.5 text-xs transition-colors focus-visible:ring-2 focus-visible:outline-none"
                >
                  <div className="flex min-w-0 flex-1 items-center gap-2 truncate">
                    <Users className="text-primary h-3.5 w-3.5 shrink-0" />
                    <span className="text-foreground truncate font-medium">{a.name}</span>
                  </div>
                  <span className="text-muted-foreground shrink-0 text-[10px]">
                    {a.bookCount} {a.bookCount === 1 ? "book" : "books"}
                  </span>
                </Link>
              ))}
            </div>
          )}

          {results.series.length > 0 && (
            <div>
              <div className="text-muted-foreground px-2 py-1 text-[11px] font-semibold uppercase">
                Series
              </div>
              {results.series.map((s) => (
                <Link
                  key={`series-${s.name}`}
                  to="/library/series/$seriesName"
                  params={{ seriesName: s.name }}
                  onClick={handleSelect}
                  className="hover:bg-accent focus-visible:ring-ring flex cursor-pointer items-center justify-between gap-2 rounded px-2 py-1.5 text-xs transition-colors focus-visible:ring-2 focus-visible:outline-none"
                >
                  <div className="flex min-w-0 flex-1 items-center gap-2 truncate">
                    <BookMarked className="text-primary h-3.5 w-3.5 shrink-0" />
                    <span className="text-foreground truncate font-medium">{s.name}</span>
                  </div>
                  <span className="text-muted-foreground shrink-0 text-[10px]">
                    {s.bookCount} {s.bookCount === 1 ? "book" : "books"}
                  </span>
                </Link>
              ))}
            </div>
          )}

          <button
            type="button"
            onClick={() => goToSearchResults(debouncedQuery)}
            className="hover:bg-accent focus-visible:ring-ring border-border mt-2 flex w-full cursor-pointer items-center gap-2 rounded border-t px-2 py-1.5 pt-2.5 text-left text-xs transition-colors focus-visible:ring-2 focus-visible:outline-none"
          >
            <Search className="text-primary h-3.5 w-3.5 shrink-0" />
            <span className="text-foreground truncate">
              See all results for &quot;{debouncedQuery}&quot;
            </span>
          </button>
        </div>
      )}
    </div>
  );
}

export default LibrarySearch;
