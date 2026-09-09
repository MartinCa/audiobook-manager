import { useEffect, useRef, useState } from "react";
import { Link } from "@tanstack/react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Tag, BookOpen, ChevronRight, Globe, Loader2, Search, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { PAGE_SIZE } from "@/constants/paging";
import { OperationProgressBar } from "./OperationProgressBar";
import { missingTagsApi, operationsApi } from "@/services/api";
import { useMissingTagSelection } from "@/hooks/useMissingTagSelection";
import { useClampedPage } from "@/hooks/useClampedPage";
import { handleApiError } from "@/lib/api";
import type { AudiobookMissingTags } from "@/types/MissingTag";
import { toast } from "sonner";

export function MissingTags() {
  const queryClient = useQueryClient();

  const { data: fields = [], isLoading: loadingFields } = useQuery({
    queryKey: ["missingTagFields"],
    queryFn: () => missingTagsApi.getFields(),
  });

  const [selectedFields, setSelectedFields] = useMissingTagSelection(fields);

  // The list is paged server-side and the search runs in SQL (accent-insensitive), so only the
  // requested page of matching books crosses the wire - a book missing even one selected critical
  // tag used to make the whole result set load and render at once.
  const [page, setPage] = useState(0);
  const [searchQuery, setSearchQuery] = useState("");
  const [search, setSearch] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => {
      const trimmed = searchQuery.trim();
      if (trimmed !== search) {
        setSearch(trimmed);
        setPage(0);
      }
    }, 300);
    return () => clearTimeout(timer);
  }, [searchQuery, search]);

  const { data: pageData, isLoading: loadingBooks } = useQuery({
    queryKey: ["missingTagsAudiobooks", selectedFields, page, search],
    queryFn: () =>
      missingTagsApi.getAudiobooksMissingTags(selectedFields, {
        page,
        pageSize: PAGE_SIZE,
        search,
      }),
    enabled: selectedFields.length > 0,
    placeholderData: keepPreviousData,
  });

  const audiobooks = (pageData?.items ?? []) as AudiobookMissingTags[];
  const totalCount = pageData?.totalCount ?? 0;
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount - 1);

  // A backfill completion or an external change can shrink the result set while the user sits
  // on a later page; pull the raw page back into range so the next fetch lands on a valid page.
  useClampedPage(page, pageCount, setPage);

  const { data: backfillStatus } = useQuery({
    queryKey: ["languageBackfillStatus"],
    queryFn: () => operationsApi.getStatus("language-backfill"),
    refetchInterval: (query) => (query.state.data?.isRunning ? 1500 : false),
  });

  const prevRunningRef = useRef(false);

  useEffect(() => {
    const isRunning = Boolean(backfillStatus?.isRunning);
    if (prevRunningRef.current && !isRunning) {
      toast.success("Language backfill operation completed");
      // A backfill fills in languages, so it can only shrink this list - drop back to page 0 so
      // the refetch never asks for a page the smaller result set no longer has.
      setPage(0);
      void queryClient.invalidateQueries({
        queryKey: ["missingTagsAudiobooks"],
      });
    }
    prevRunningRef.current = isRunning;
  }, [backfillStatus?.isRunning, queryClient]);

  const toggleField = (key: string) => {
    setSelectedFields(
      selectedFields.includes(key)
        ? selectedFields.filter((k) => k !== key)
        : [...selectedFields, key],
    );
    setPage(0);
  };

  const selectCriticalOnly = () => {
    setSelectedFields(fields.filter((f) => f.isCriticalByDefault).map((f) => f.key));
    setPage(0);
  };

  const selectAllFields = () => {
    setSelectedFields(fields.map((f) => f.key));
    setPage(0);
  };

  const clearSelection = () => {
    setSelectedFields([]);
    setPage(0);
  };

  const handleStartLanguageBackfill = async () => {
    try {
      await missingTagsApi.startLanguageBackfill();
      toast.success("Language backfill started in background");
      void queryClient.invalidateQueries({ queryKey: ["languageBackfillStatus"] });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    }
  };

  const isBackfillRunning = Boolean(backfillStatus?.isRunning);

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <Button variant="ghost" size="sm" render={<Link to="/library" />}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </Button>

        <Button
          variant="outline"
          onClick={() => {
            void handleStartLanguageBackfill();
          }}
          disabled={isBackfillRunning}
        >
          <Globe className={`mr-2 h-4 w-4 ${isBackfillRunning ? "animate-spin" : ""}`} />
          {isBackfillRunning ? "Backfilling Languages..." : "Backfill Missing Languages"}
        </Button>
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <Tag className="text-primary h-6 w-6" />
          Missing Tags Inspection
        </h1>
        <p className="text-muted-foreground text-sm">
          Find audiobooks in your library missing critical tags such as author, year, narrator, or
          language.
        </p>
      </div>

      {isBackfillRunning && backfillStatus && (
        <OperationProgressBar
          processed={backfillStatus.processed}
          total={backfillStatus.total}
          label="Backfilling missing languages..."
        />
      )}

      <div className="space-y-2">
        <div className="flex flex-wrap items-center gap-2 sm:gap-3">
          <label className="text-muted-foreground text-xs font-semibold uppercase">
            Select Fields to Inspect
          </label>
          <Button
            variant="link"
            size="sm"
            className="h-auto p-0 text-xs"
            onClick={selectCriticalOnly}
          >
            Critical only
          </Button>
          <Button variant="link" size="sm" className="h-auto p-0 text-xs" onClick={selectAllFields}>
            Select all
          </Button>
          <Button variant="link" size="sm" className="h-auto p-0 text-xs" onClick={clearSelection}>
            Clear
          </Button>
        </div>
        <div className="flex flex-wrap gap-2">
          {fields.map((f) => {
            const isSelected = selectedFields.includes(f.key);
            return (
              <Badge
                key={f.key}
                variant={isSelected ? "default" : "outline"}
                className="hover:bg-primary/90 cursor-pointer px-3 py-1.5 select-none"
                onClick={() => toggleField(f.key)}
              >
                {f.label}
              </Badge>
            );
          })}
        </div>
      </div>

      {selectedFields.length > 0 && (
        <div className="relative max-w-md">
          <Search className="text-muted-foreground absolute top-2.5 left-3 h-4 w-4" />
          <Input
            placeholder="Filter by book title..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="pr-9 pl-9"
          />
          {searchQuery ? (
            <button
              type="button"
              onClick={() => setSearchQuery("")}
              aria-label="Clear search"
              className="text-muted-foreground hover:text-foreground absolute top-2.5 right-2.5 cursor-pointer rounded-sm p-0.5 transition-colors"
            >
              <X className="h-4 w-4" />
            </button>
          ) : null}
        </div>
      )}

      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-foreground text-lg font-bold">
            Audiobooks with Missing Tags ({totalCount})
          </h2>
        </div>

        {loadingFields || (loadingBooks && audiobooks.length === 0) ? (
          <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
            <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
            <p className="text-sm">Scanning tags...</p>
          </div>
        ) : totalCount === 0 ? (
          <Card className="p-12 text-center">
            <BookOpen className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
            <h3 className="text-foreground text-lg font-medium">
              No audiobooks missing selected tags
            </h3>
            <p className="text-muted-foreground mt-1 text-sm">
              {search
                ? "No books match your title filter."
                : "Every book in your library contains the selected tag fields."}
            </p>
          </Card>
        ) : (
          <div className="space-y-2">
            {audiobooks.map((b) => (
              <Link
                key={b.audiobookId}
                to="/library/book/$bookId"
                params={{ bookId: String(b.audiobookId) }}
                className="group border-border bg-card hover:bg-muted/50 flex items-center justify-between rounded-lg border p-3 transition-colors"
              >
                <div className="min-w-0 flex-1">
                  <div className="text-foreground font-semibold break-words">
                    {b.authors.join(", ")} &mdash; {b.bookName}
                  </div>
                  <div className="flex flex-wrap gap-1.5 pt-1">
                    {b.missingFields.map((f) => (
                      <Badge
                        key={f}
                        variant="secondary"
                        className="bg-amber-500/15 text-[10px] text-amber-600 dark:text-amber-400"
                      >
                        Missing {f}
                      </Badge>
                    ))}
                  </div>
                </div>

                <ChevronRight className="text-muted-foreground group-hover:text-foreground h-4 w-4 shrink-0" />
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
    </div>
  );
}

export default MissingTags;
