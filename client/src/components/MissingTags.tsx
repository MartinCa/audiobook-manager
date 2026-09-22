import { useEffect, useRef, useState } from "react";
import { Link } from "@tanstack/react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Tag, BookOpen, Globe } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { PAGE_SIZE } from "@/constants/paging";
import { OperationKeys } from "@/constants/signalrEvents";
import { LinkButton } from "./LinkButton";
import { OperationProgressBar } from "./OperationProgressBar";
import { OwnedBookList } from "./library/OwnedBookList";
import { missingTagsApi, operationsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useMissingTagSelection } from "@/hooks/useMissingTagSelection";
import { useClampedPage } from "@/hooks/useClampedPage";
import { useBookSelection } from "@/hooks/useBookSelection";
import { handleApiError } from "@/lib/api";
import type { AudiobookMissingTags } from "@/types/MissingTag";
import type { BookListFilters } from "@/types/EntityFilters";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";
import { notifications } from "@/lib/notifications";

function toManagedAudiobook(b: AudiobookMissingTags): ManagedAudiobook {
  return {
    id: b.audiobookId,
    bookName: b.bookName,
    authors: b.authors,
    narrators: b.narrators,
    genres: [],
    year: b.year,
    series: b.series,
    seriesPart: b.seriesPart,
    coverFilePath: b.coverFilePath,
    durationInSeconds: b.durationInSeconds,
    isMatched: b.isMatched,
    matchedSourceName: b.matchedSourceName,
  };
}

export function MissingTags() {
  const queryClient = useQueryClient();
  const selection = useBookSelection();

  const { data: fields = [], isLoading: loadingFields } = useQuery({
    queryKey: queryKeys.missingTagFields(),
    queryFn: () => missingTagsApi.getFields(),
  });

  const [selectedFields, setSelectedFields] = useMissingTagSelection(fields);

  // The list is paged server-side and the search runs in SQL (accent-insensitive), so only the
  // requested page of matching books crosses the wire - a book missing even one selected critical
  // tag used to make the whole result set load and render at once.
  const [page, setPage] = useState(0);
  const [search, setSearch] = useState("");
  const [filters, setFilters] = useState<BookListFilters>({});

  const handleFiltersChange = (next: BookListFilters) => {
    setFilters(next);
    setPage(0);
  };

  const { data: pageData, isLoading: loadingBooks } = useQuery({
    queryKey: queryKeys.missingTagsAudiobooks.page(selectedFields, page, search, filters),
    queryFn: () =>
      missingTagsApi.getAudiobooksMissingTags(selectedFields, {
        page,
        pageSize: PAGE_SIZE,
        search,
        filters,
      }),
    enabled: selectedFields.length > 0,
    placeholderData: keepPreviousData,
  });

  const audiobooks = (pageData?.items ?? []) as AudiobookMissingTags[];
  const books = audiobooks.map(toManagedAudiobook);
  const missingFieldsByBookId = new Map(audiobooks.map((b) => [b.audiobookId, b.missingFields]));
  const totalCount = pageData?.totalCount ?? 0;
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const currentPage = Math.min(page, pageCount - 1);

  // A backfill completion or an external change can shrink the result set while the user sits
  // on a later page; pull the raw page back into range so the next fetch lands on a valid page.
  useClampedPage(page, pageCount, setPage);

  const { data: backfillStatus } = useQuery({
    queryKey: queryKeys.languageBackfillStatus(),
    queryFn: () => operationsApi.getStatus(OperationKeys.languageBackfill),
    refetchInterval: (query) => (query.state.data?.isRunning ? 1500 : false),
  });

  const prevRunningRef = useRef(false);

  useEffect(() => {
    const isRunning = Boolean(backfillStatus?.isRunning);
    if (prevRunningRef.current && !isRunning) {
      notifications.success("Language backfill operation completed");
      // A backfill fills in languages, so it can only shrink this list - drop back to page 0 so
      // the refetch never asks for a page the smaller result set no longer has.
      setPage(0);
      void queryClient.invalidateQueries({
        queryKey: queryKeys.missingTagsAudiobooks.all(),
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
      notifications.success("Language backfill started in background");
      void queryClient.invalidateQueries({ queryKey: queryKeys.languageBackfillStatus() });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    }
  };

  const isBackfillRunning = Boolean(backfillStatus?.isRunning);

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <LinkButton variant="ghost" size="sm" render={<Link to="/library" />}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Library
        </LinkButton>

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

      <div className="space-y-3">
        <h2 className="text-foreground text-lg font-bold">
          Audiobooks with Missing Tags ({totalCount})
        </h2>

        <OwnedBookList
          books={books}
          totalCount={totalCount}
          loading={loadingFields || (loadingBooks && books.length === 0)}
          loadingLabel="Scanning tags..."
          emptyState={
            selectedFields.length === 0 ? (
              <Card className="p-12 text-center">
                <Tag className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
                <h3 className="text-foreground text-lg font-medium">No fields selected</h3>
                <p className="text-muted-foreground mt-1 text-sm">
                  Select at least one field above to inspect the library for it.
                </p>
              </Card>
            ) : (
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
            )
          }
          selection={selection}
          search={search}
          onSearchChange={(value) => {
            setSearch(value);
            setPage(0);
          }}
          searchPlaceholder="Filter by book title..."
          filters={filters}
          onFiltersChange={handleFiltersChange}
          page={currentPage}
          pageCount={pageCount}
          pageSize={PAGE_SIZE}
          pagerDisabled={loadingBooks}
          onPageChange={setPage}
          itemNoun="audiobooks"
          renderExtraBadges={(book) =>
            (missingFieldsByBookId.get(book.id) ?? []).map((f) => (
              <Badge
                key={f}
                variant="secondary"
                className="bg-amber-500/15 text-[10px] text-amber-600 dark:text-amber-400"
              >
                Missing {f}
              </Badge>
            ))
          }
        />
      </div>
    </div>
  );
}

export default MissingTags;
