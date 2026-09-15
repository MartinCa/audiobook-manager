import { Link } from "@tanstack/react-router";
import { AlertCircle, CheckCircle2, ChevronRight } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { SeriesOverview } from "@/types/Series";

interface SeriesListEntryProps {
  series: SeriesOverview;
  /**
   * Forwarded to the series detail link's search. The author detail passes its authorId so the
   * detail page can filter the series' books to that author; the series overview passes none.
   */
  search?: { authorId?: number };
}

/**
 * One series row, shared by the /library/series overview and the author detail's series section:
 * the match badge (emerald "Hardcover (75%)" when matched, outline "Unmatched" otherwise), the
 * "By <authors> · N books owned" sub-line with the amber missing count, and the destructive
 * missing badge plus chevron on the right.
 */
export function SeriesListEntry({ series, search }: SeriesListEntryProps) {
  return (
    <Link
      to="/library/series/$seriesName"
      params={{ seriesName: series.name }}
      search={search}
      className="group border-border bg-card hover:bg-muted/50 focus-visible:ring-ring flex cursor-pointer items-center justify-between rounded-lg border p-3 transition-colors focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none"
    >
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-foreground font-semibold break-words">{series.name}</span>
          {series.isMatched ? (
            <Badge
              variant="secondary"
              className="gap-1 bg-emerald-500/15 text-[11px] text-emerald-600 dark:text-emerald-400"
            >
              <CheckCircle2 className="h-3 w-3" />
              {series.matchedSourceName}
              {series.matchConfidence != null && (
                <span className="ml-0.5 opacity-75">
                  ({Math.round(series.matchConfidence * 100)}%)
                </span>
              )}
            </Badge>
          ) : (
            <Badge variant="outline" className="text-muted-foreground text-[11px]">
              Unmatched
            </Badge>
          )}
        </div>

        <div className="text-muted-foreground flex flex-wrap items-center gap-x-2 text-xs">
          {series.authors && series.authors.length > 0 && (
            <span>By {series.authors.join(", ")} &middot;</span>
          )}
          <span>
            {series.ownedBookCount} {series.ownedBookCount === 1 ? "book" : "books"} owned
          </span>
          {series.isMatched && series.missingBookCount > 0 && (
            <span className="font-medium text-amber-600 dark:text-amber-400">
              &middot; {series.missingBookCount} missing
            </span>
          )}
        </div>
      </div>

      <div className="flex shrink-0 items-center gap-3">
        {series.missingBookCount > 0 && (
          <Badge variant="destructive" className="gap-1 text-xs">
            <AlertCircle className="h-3 w-3" />
            {series.missingBookCount} missing
          </Badge>
        )}
        <ChevronRight className="text-muted-foreground group-hover:text-foreground h-4 w-4" />
      </div>
    </Link>
  );
}
