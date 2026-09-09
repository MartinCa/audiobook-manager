import { Link } from "@tanstack/react-router";
import { AlertTriangle, ChevronRight, Clock, Library, RefreshCw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import { browseApi } from "@/services/api";
import { formatDuration } from "@/helpers/formatHelpers";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";

export interface BookListRowProps {
  book: ManagedAudiobook;
  /** Destructive badge with the issue count, as in the library list today. */
  issueCount?: number;
  /** "Pending refresh" secondary badge for books with a stored metadata-refresh snapshot. */
  hasPendingRefresh?: boolean;
  selectable?: boolean;
  selected?: boolean;
  onSelectedChange?: (selected: boolean) => void;
  /** "#3 " prefix on the title (SeriesDetail context, where the row's own series name is hidden). */
  showSeriesPart?: boolean;
  /** Skip the "Series: ..." meta span; used inside SeriesDetail where the series is the heading. */
  hideSeries?: boolean;
}

export function BookListRow({
  book,
  issueCount,
  hasPendingRefresh,
  selectable = false,
  selected = false,
  onSelectedChange,
  showSeriesPart = false,
  hideSeries = false,
}: BookListRowProps) {
  return (
    <div className="group border-border bg-card hover:bg-muted/50 flex items-center gap-3 rounded-lg border p-3">
      {selectable && (
        <Checkbox
          aria-label={`Select ${book.bookName ?? "book"}`}
          checked={selected}
          onCheckedChange={(checked) => onSelectedChange?.(Boolean(checked))}
          className="shrink-0"
        />
      )}
      <Link
        to="/library/book/$bookId"
        params={{ bookId: String(book.id) }}
        className="flex min-w-0 flex-1 cursor-pointer items-center justify-between gap-3"
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
                <Library className="h-6 w-6" />
              </div>
            )}
          </div>

          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-foreground max-w-full min-w-0 truncate font-semibold">
                {showSeriesPart && book.seriesPart ? `#${book.seriesPart} ` : ""}
                {book.bookName}
              </span>
              {book.year && <span className="text-muted-foreground text-xs">({book.year})</span>}
              {typeof issueCount === "number" && issueCount > 0 && (
                <Badge variant="destructive" className="h-5 gap-1 px-1.5 text-[10px]">
                  <AlertTriangle className="h-2.5 w-2.5" />
                  {issueCount} {issueCount === 1 ? "issue" : "issues"}
                </Badge>
              )}
              {hasPendingRefresh && (
                <Badge variant="secondary" className="h-5 gap-1 px-1.5 text-[10px]">
                  <RefreshCw className="h-2.5 w-2.5" />
                  Pending refresh
                </Badge>
              )}
            </div>

            <div className="text-muted-foreground flex flex-wrap items-center gap-x-2 text-xs">
              {book.authors && book.authors.length > 0 && (
                <span>By {book.authors.join(", ")} &middot;</span>
              )}
              {!hideSeries && book.series && (
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
      </Link>
    </div>
  );
}
