import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { Search, X } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { Skeleton } from "@/components/ui/skeleton";
import { BookListRow } from "./BookListRow";
import { BookBulkActionBar } from "./BookBulkActionBar";
import { SectionPager } from "./SectionPager";
import { EntityFilterBar, type FilterFieldDef } from "@/components/filters/EntityFilterBar";
import { countActiveFilters } from "@/components/filters/filterUtils";
import { FilterToggleButton } from "@/components/filters/FilterToggleButton";
import { browseApi, consistencyApi, metadataRefreshApi, settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { languageLabel } from "@/helpers/languages";
import { SOURCE_OPTION_LABELS, UNSUPPORTED_SOURCE_VALUE } from "@/types/EntityFilters";
import type { BookListFilters } from "@/types/EntityFilters";
import type { BookSelection } from "@/hooks/useBookSelection";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";

// The duration filter is entered/displayed in minutes but stored (filter state, and the backend's
// minDurationInSeconds/maxDurationInSeconds query params - see BookSummaryFilter and
// AudiobookRepository.ApplyBookSummaryFilter) in seconds (Bug 7). Rounding on toDisplay covers a
// value that arrived from a hand-edited/older URL and isn't an exact multiple of 60.
const DURATION_FILTER_UNIT = {
  toDisplay: (storedSeconds: number) => Math.round(storedSeconds / 60),
  toStored: (displayMinutes: number) => displayMinutes * 60,
};

/** Typed so a failed summary fetch still indexes as a count map rather than widening to {}. */
const NO_ISSUE_COUNTS: Record<number, number> = {};

/** Pending-metadata ids only gain members through a refresh; an empty set is the safe fallback. */
const NO_PENDING_IDS: number[] = [];

export interface OwnedBookListProps {
  /** The current page's rows - already filtered/searched/paged by the caller's own query. */
  books: ManagedAudiobook[];
  /** Total row count across every page of the current search/filter, for the pager and the count line. */
  totalCount: number;
  /** True only while the very first page of a fresh search/filter is still loading (no rows to show yet). */
  loading?: boolean;
  /** aria-label on the loading skeleton's status region. */
  loadingLabel?: string;
  /** Rendered instead of the row list when there are no rows and loading is false. */
  emptyState: ReactNode;

  /** Selection state - each surface owns its own useBookSelection() instance and its own reset rules. */
  selection: BookSelection;

  /** The committed search value (e.g. a route's `q` param); this component owns the debounce/typing UI. */
  search: string;
  onSearchChange: (value: string) => void;
  searchPlaceholder?: string;
  /**
   * False hides the search input (e.g. SearchResultsPage's books tab, which already has its own
   * page-level search box driving `search`/`onSearchChange` for every tab) while keeping every
   * other piece - filters, badges, selection, pagination - shared. Default true.
   */
  showSearchBox?: boolean;

  /** The committed filter values (e.g. route search params or local state); this component owns the UI. */
  filters: BookListFilters;
  onFiltersChange: (next: BookListFilters) => void;

  /** 0-indexed current page, matching SectionPager. */
  page: number;
  pageCount: number;
  pageSize?: number;
  onPageChange: (page: number) => void;
  pagerDisabled?: boolean;

  /** "#3 " prefix on the title (SeriesDetail's owned-books context). */
  showSeriesPart?: boolean;
  /** Skip the "Series: ..." meta line (SeriesDetail's owned-books context, where it's redundant). */
  hideSeries?: boolean;

  /** Noun used in the "Showing N of Total {itemNoun}" line - default "audiobooks". */
  itemNoun?: string;
  skeletonRowCount?: number;
}

/**
 * The shared list UI for every place owned books are listed (library, series detail's owned
 * section, author detail's standalone section, search results' books tab) - Bug 8: these had
 * drifted into four hand-rolled lists with different filtering, badges and pagination.
 *
 * Owns: the debounced text search input, the EntityFilterBar-driven option filters (metadata
 * source/genre/language/duration - identical everywhere, so filtering behaves the same on every
 * surface), BookListRow rendering with per-surface customization (showSeriesPart/hideSeries),
 * the issue-count/pending-refresh badges (fetched here so every surface gets them, not just the
 * library list), the select-page checkbox + BookBulkActionBar wiring, and pagination via
 * SectionPager.
 *
 * Deliberately does NOT fetch the book rows themselves: SeriesDetail and AuthorDetail compute
 * their owned/standalone section alongside several other sections in one combined backend call
 * (to avoid N+1 requests as each section pages independently), so the caller runs its own query
 * and passes the current page's rows down, with page/filter/search state lifted to the caller
 * (its query key needs to include them to refetch correctly).
 */
export function OwnedBookList({
  books,
  totalCount,
  loading = false,
  loadingLabel = "Loading audiobooks...",
  emptyState,
  selection,
  search,
  onSearchChange,
  searchPlaceholder = "Search title, author, series, narrator...",
  showSearchBox = true,
  filters,
  onFiltersChange,
  page,
  pageCount,
  pageSize,
  onPageChange,
  pagerDisabled = false,
  showSeriesPart = false,
  hideSeries = false,
  itemNoun = "audiobooks",
  skeletonRowCount = 6,
}: OwnedBookListProps) {
  // Source options come from whichever scrapers are actually registered (see
  // BrowseController.GetFilterOptions), and genre/language options from what's actually present
  // in the library - never a hardcoded list. Shared across every mounted OwnedBookList through the
  // same query key, so only one fetch happens regardless of how many lists are on screen.
  const filterOptionsQuery = useQuery({
    queryKey: queryKeys.browseFilterOptions(),
    queryFn: () => browseApi.getFilterOptions(),
    staleTime: 5 * 60 * 1000,
  });

  const languagesQuery = useQuery({
    queryKey: queryKeys.languages(),
    queryFn: () => settingsApi.getLanguages(),
  });

  // Issue-count and pending-refresh badges: fetched once here (globally cached, like the filter
  // options above) rather than folded into each surface's own page query, so every owned-book
  // list gets the same badges the library list used to be the only one to show.
  const issueSummaryQuery = useQuery({
    queryKey: queryKeys.consistency.issueSummary(),
    queryFn: () => consistencyApi.getIssueSummary().catch(() => NO_ISSUE_COUNTS),
  });
  const pendingSummaryQuery = useQuery({
    queryKey: queryKeys.metadataRefresh.pendingSummary(),
    queryFn: () => metadataRefreshApi.getPendingSummary().catch(() => NO_PENDING_IDS),
  });
  const issueSummary = issueSummaryQuery.data ?? NO_ISSUE_COUNTS;
  const pendingRefreshIds = new Set(pendingSummaryQuery.data ?? NO_PENDING_IDS);

  const languageOptions = filterOptionsQuery.data?.languages ?? [];
  const languageLabels = Object.fromEntries(
    languageOptions.map((code) => [
      code,
      languageLabel(code, languagesQuery.data?.languages ?? []),
    ]),
  );

  const FILTER_FIELDS: FilterFieldDef[] = [
    {
      type: "multiselect",
      key: "sources",
      label: "Metadata source",
      options: filterOptionsQuery.data?.sources ?? [],
      optionLabels: SOURCE_OPTION_LABELS,
      selectAllOption: { label: "Any supported", excludeValues: [UNSUPPORTED_SOURCE_VALUE] },
    },
    {
      type: "multiselect",
      key: "genres",
      label: "Genre",
      options: filterOptionsQuery.data?.genres ?? [],
    },
    {
      type: "multiselect",
      key: "languages",
      label: "Language",
      options: languageOptions,
      optionLabels: languageLabels,
    },
    {
      type: "numberRange",
      label: "Duration (minutes)",
      minKey: "minDurationInSeconds",
      maxKey: "maxDurationInSeconds",
      unit: DURATION_FILTER_UNIT,
    },
  ];

  // Collapsed by default; a filter already active on load (a shared/bookmarked URL, or a filter
  // set before this instance mounted) starts expanded so the list isn't filtered with no visible
  // explanation.
  const [filtersExpanded, setFiltersExpanded] = useState(
    () => countActiveFilters(FILTER_FIELDS, filters) > 0,
  );
  const filterPanelId = useId();

  // --- Search box: local typing state debounces into the committed `search` prop, mirroring the
  // library list's original search box (immediate Enter/clear, 300ms debounce otherwise). A ref
  // holds the latest onSearchChange so the debounce effect doesn't need it as a dependency - an
  // inline callback identity changing every render must not reset an in-flight timer.
  const [prevSearch, setPrevSearch] = useState(search);
  const [searchQuery, setSearchQuery] = useState(search);
  const onSearchChangeRef = useRef(onSearchChange);
  useEffect(() => {
    onSearchChangeRef.current = onSearchChange;
  });

  if (prevSearch !== search) {
    setPrevSearch(search);
    if (searchQuery.trim() !== search) {
      setSearchQuery(search);
    }
  }

  useEffect(() => {
    const timer = setTimeout(() => {
      const trimmed = searchQuery.trim();
      if (trimmed !== search) {
        onSearchChangeRef.current(trimmed);
      }
    }, 300);
    return () => clearTimeout(timer);
  }, [searchQuery, search]);

  const handleClearSearch = () => {
    setSearchQuery("");
    if (search) {
      onSearchChange("");
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        {showSearchBox && (
          <div className="relative max-w-md flex-1">
            <Search className="text-muted-foreground absolute top-2.5 left-3 h-4 w-4" />
            <Input
              placeholder={searchPlaceholder}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  const trimmed = searchQuery.trim();
                  if (trimmed !== search) {
                    onSearchChange(trimmed);
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
        )}

        <FilterToggleButton
          expanded={filtersExpanded}
          onToggle={() => setFiltersExpanded((prev) => !prev)}
          activeCount={countActiveFilters(FILTER_FIELDS, filters)}
          controls={filterPanelId}
        />

        <div className="flex items-center gap-3">
          <Checkbox
            id={`${filterPanelId}-select-page`}
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
            htmlFor={`${filterPanelId}-select-page`}
            className="text-muted-foreground cursor-pointer text-xs leading-none select-none"
          >
            Select page
          </label>
          <div className="text-muted-foreground text-xs">
            Showing {books.length} of {totalCount} {itemNoun}
          </div>
        </div>
      </div>

      {filtersExpanded && (
        <div id={filterPanelId}>
          <EntityFilterBar fields={FILTER_FIELDS} values={filters} onChange={onFiltersChange} />
        </div>
      )}

      {loading && books.length === 0 ? (
        <div role="status" aria-label={loadingLabel} className="space-y-2">
          {Array.from({ length: skeletonRowCount }, (_, i) => (
            <div
              key={i}
              className="border-border bg-card flex items-center gap-3 rounded-lg border p-3"
            >
              <Skeleton className="size-4 shrink-0 rounded-[4px]" />
              <Skeleton className="h-12 w-12 shrink-0 rounded" />
              <div className="min-w-0 flex-1 space-y-2">
                <Skeleton className="h-4 w-1/2 max-w-80" />
                <Skeleton className="h-3 w-2/3 max-w-96" />
              </div>
              <Skeleton className="size-4 shrink-0" />
            </div>
          ))}
        </div>
      ) : books.length === 0 ? (
        emptyState
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
                showSeriesPart={showSeriesPart}
                hideSeries={hideSeries}
                selectable
                selected={selection.isSelected(book.id)}
                onSelectedChange={() => selection.toggle(book)}
              />
            );
          })}
        </div>
      )}

      <BookBulkActionBar selection={selection} />

      {pageCount > 1 && (
        <SectionPager
          currentPage={page}
          pageCount={pageCount}
          totalCount={totalCount}
          pageSize={pageSize}
          disabled={pagerDisabled}
          onPageChange={onPageChange}
        />
      )}
    </div>
  );
}

export default OwnedBookList;
