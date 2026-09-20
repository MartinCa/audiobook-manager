import { BookPlus, ExternalLink, Eye, EyeOff, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PAGE_SIZE } from "@/constants/paging";
import { cn } from "cn";
import { formatDate } from "@/helpers/formatHelpers";
import { isBookUpcoming } from "@/helpers/expectedBooks";
import type { ExpectedBookRow } from "@/helpers/expectedBooks";

interface ExpectedBookListProps {
  /** Which section this instance renders - drives the row tone and where the scope's ignored
   *  entries land when "show ignored" is on (an ignored row is classified Missing or Upcoming by
   *  the same release-date rule the backend uses). */
  section: "missing" | "upcoming";
  /** The section's active (non-ignored) entries. */
  items: ExpectedBookRow[];
  /** Every ignored entry in the scope, rendered faded at the bottom of this list when
   *  `showIgnored` is on and the row classifies as this section. */
  ignoredItems: ExpectedBookRow[];
  /** Total ignored entries in the scope - drives the overflow note when the loaded ignored page
   *  does not carry all of them (and the pager's total when one is wired). */
  ignoredTotal: number;
  /** Whether dismissed entries render faded within this list (one scope-level toggle). */
  showIgnored: boolean;
  /** Optional pager for the ignored sub-list (same props shape as SectionPager, minus
   *  totalCount, which is always ignoredTotal). Rendered in place of the static
   *  "...and N more ignored books..." note when the scope's ignored section pages server-side
   *  (SeriesDetail) and the loaded page does not carry every entry; the author detail passes no
   *  pager because its endpoint returns the full in-memory ignored list. */
  ignoredPager?: {
    currentPage: number;
    pageCount: number;
    onPageChange: (page: number) => void;
  };
  /** id of the row currently busy with an ignore/unignore call, or null. */
  busyBookId: number | null;
  /** Shown when there is nothing to render in this section. */
  emptyMessage: string;
  onIgnore: (book: ExpectedBookRow) => void;
  onUnignore: (book: ExpectedBookRow) => void;
  /** Per-row action for the series scope's Missing list only (an unreleased row cannot be in the
   *  library yet, and an ignored row stays addressable through its Unignore action alone). */
  onFindInLibrary?: (book: ExpectedBookRow) => void;
}

/** One missing/upcoming (or, with "show ignored", ignored too) books list, shared by the author
 * and series detail pages. Rows render title, year/precise release date, series name/position
 * when present, a source link, and the scope's ignore/unignore action; dismissed rows render
 * faded within the same list instead of only in a separate section. The call sites differ only
 * by the ignore/unignore endpoint functions they wire in - everything else lives here.
 */
export function ExpectedBookList({
  section,
  items,
  ignoredItems,
  ignoredTotal,
  showIgnored,
  ignoredPager,
  busyBookId,
  emptyMessage,
  onIgnore,
  onUnignore,
  onFindInLibrary,
}: ExpectedBookListProps) {
  const visibleIgnored = showIgnored
    ? ignoredItems.filter(
        (book) => isBookUpcoming(book.releaseDate, book.year) === (section === "upcoming"),
      )
    : [];
  const rows = [...items, ...visibleIgnored];

  if (rows.length === 0) {
    return <p className="text-muted-foreground text-xs">{emptyMessage}</p>;
  }

  const paged = showIgnored && ignoredPager && ignoredPager.pageCount > 1;
  // The static overflow note only stands in for a pager: once the ignored section pages (a
  // pager is wired), the pager is how a user reaches the rest, so a redundant count note over
  // the same entries would just repeat what the pager's total already says.
  const staleIgnoredCount = showIgnored && !paged ? ignoredTotal - ignoredItems.length : 0;

  return (
    <div className="space-y-2">
      {rows.map((book) => (
        <ExpectedBookRowView
          key={book.id}
          book={book}
          section={section}
          busyBookId={busyBookId}
          onIgnore={onIgnore}
          onUnignore={onUnignore}
          onFindInLibrary={onFindInLibrary}
        />
      ))}
      {staleIgnoredCount > 0 && (
        <p className="text-muted-foreground text-xs">
          and {staleIgnoredCount} more ignored book{staleIgnoredCount === 1 ? "" : "s"}...
        </p>
      )}
      {paged && (
        <SectionPager
          currentPage={ignoredPager.currentPage}
          pageCount={ignoredPager.pageCount}
          totalCount={ignoredTotal}
          onPageChange={ignoredPager.onPageChange}
        />
      )}
    </div>
  );
}

/** One row of the shared list: title (with the position prefix), year/precise release date,
 * series name when the row carries one, a source link, and Ignore or Unignore (an ignored row
 * renders faded and only offers to restore itself). */
function ExpectedBookRowView({
  book,
  section,
  busyBookId,
  onIgnore,
  onUnignore,
  onFindInLibrary,
}: {
  book: ExpectedBookRow;
  section: "missing" | "upcoming";
  busyBookId: number | null;
  onIgnore: (book: ExpectedBookRow) => void;
  onUnignore: (book: ExpectedBookRow) => void;
  onFindInLibrary?: (book: ExpectedBookRow) => void;
}) {
  const ignored = book.isIgnored;
  const toneClasses =
    section === "missing" ? "border-amber-500/20 bg-amber-500/5" : "border-border bg-card";
  const seriesName = book.seriesName ?? book.sourceSeriesName;

  return (
    <div
      className={cn(
        "flex flex-col justify-between gap-2 rounded-lg border p-3 text-xs sm:flex-row sm:items-center",
        toneClasses,
        ignored && "opacity-75",
      )}
    >
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-1.5">
          {seriesName && (
            <span className="text-muted-foreground font-medium break-words">{seriesName}</span>
          )}
          <span className={cn("font-semibold break-words", ignored && "text-muted-foreground")}>
            {book.position ? `Part ${book.position} — ` : ""}
            {book.title}
          </span>
        </div>
        <div className="text-muted-foreground">
          {ignored && <span>ignored</span>}
          {book.year && <span> ({book.year})</span>}
          {book.releaseDate && <span> · releases {formatDate(book.releaseDate)}</span>}
        </div>
      </div>
      <div className="flex shrink-0 items-center gap-2 self-end sm:self-center">
        {book.sourceUrl && (
          <a
            href={book.sourceUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="text-primary flex items-center hover:underline"
          >
            <ExternalLink className="mr-1 h-3 w-3" />
            Source
          </a>
        )}
        {!ignored && onFindInLibrary && (
          <Button
            variant="secondary"
            size="sm"
            className="h-6 text-[11px]"
            onClick={() => onFindInLibrary(book)}
          >
            <BookPlus className="mr-1 h-3 w-3" />
            Find in Library
          </Button>
        )}
        {ignored ? (
          <Button
            variant="outline"
            size="sm"
            className="h-6 self-end text-[11px] sm:self-center"
            disabled={busyBookId === book.id}
            onClick={() => onUnignore(book)}
          >
            {busyBookId === book.id ? (
              <Loader2 className="mr-1 h-3 w-3 animate-spin" />
            ) : (
              <Eye className="mr-1 h-3 w-3" />
            )}
            Unignore
          </Button>
        ) : (
          <Button
            variant="ghost"
            size="sm"
            className="h-6 text-[11px]"
            disabled={busyBookId === book.id}
            onClick={() => onIgnore(book)}
          >
            {busyBookId === book.id ? (
              <Loader2 className="mr-1 h-3 w-3 animate-spin" />
            ) : (
              <EyeOff className="mr-1 h-3 w-3" />
            )}
            Ignore
          </Button>
        )}
      </div>
    </div>
  );
}

/** The clamped pager each paged section renders, in the CleanBookUrls shape. Shared by the
 * series detail's paged sections and the author detail's missing-series section. */
export function SectionPager({
  currentPage,
  pageCount,
  totalCount,
  onPageChange,
}: {
  currentPage: number;
  pageCount: number;
  totalCount: number;
  onPageChange: (page: number) => void;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
      <span className="text-muted-foreground text-xs">
        Showing {currentPage * PAGE_SIZE + 1}–{Math.min((currentPage + 1) * PAGE_SIZE, totalCount)}{" "}
        of {totalCount}
      </span>
      <div className="flex items-center gap-2">
        <Button
          size="sm"
          variant="outline"
          disabled={currentPage === 0}
          onClick={() => onPageChange(currentPage - 1)}
        >
          Previous
        </Button>
        <Button
          size="sm"
          variant="outline"
          disabled={currentPage >= pageCount - 1}
          onClick={() => onPageChange(currentPage + 1)}
        >
          Next
        </Button>
      </div>
    </div>
  );
}
