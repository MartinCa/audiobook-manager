import { useState, useEffect, useRef } from "react";
import { useNavigate } from "react-router-dom";
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

  const handleSelect = (path: string) => {
    setFocused(false);
    setQuery("");
    void navigate(path);
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
                <div
                  key={`book-${b.id}`}
                  onClick={() => handleSelect(`/library/book/${b.id}`)}
                  className="hover:bg-accent flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 text-xs transition-colors"
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
                </div>
              ))}
            </div>
          )}

          {results.authors.length > 0 && (
            <div className="mb-2">
              <div className="text-muted-foreground px-2 py-1 text-[11px] font-semibold uppercase">
                Authors
              </div>
              {results.authors.map((a) => (
                <div
                  key={`author-${a.id}`}
                  onClick={() => handleSelect(`/library/authors/${a.id}`)}
                  className="hover:bg-accent flex cursor-pointer items-center justify-between rounded px-2 py-1.5 text-xs transition-colors"
                >
                  <div className="flex items-center gap-2 truncate">
                    <Users className="text-primary h-3.5 w-3.5 shrink-0" />
                    <span className="text-foreground font-medium">{a.name}</span>
                  </div>
                  <span className="text-muted-foreground text-[10px]">
                    {a.bookCount} {a.bookCount === 1 ? "book" : "books"}
                  </span>
                </div>
              ))}
            </div>
          )}

          {results.series.length > 0 && (
            <div>
              <div className="text-muted-foreground px-2 py-1 text-[11px] font-semibold uppercase">
                Series
              </div>
              {results.series.map((s) => (
                <div
                  key={`series-${s.name}`}
                  onClick={() => handleSelect(`/library/series/${encodeURIComponent(s.name)}`)}
                  className="hover:bg-accent flex cursor-pointer items-center justify-between rounded px-2 py-1.5 text-xs transition-colors"
                >
                  <div className="flex items-center gap-2 truncate">
                    <BookMarked className="text-primary h-3.5 w-3.5 shrink-0" />
                    <span className="text-foreground font-medium">{s.name}</span>
                  </div>
                  <span className="text-muted-foreground text-[10px]">
                    {s.bookCount} {s.bookCount === 1 ? "book" : "books"}
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default LibrarySearch;
