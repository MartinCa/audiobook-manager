import { CheckCircle2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";

export interface MatchSourceBadgeProps {
  /** Nullable/optional because some rows are built from a narrower source with no match-source
   * fields at all (see ManagedAudiobook/AuthorSummary's Require<> doc) - treated as unmatched. */
  isMatched: boolean | null | undefined;
  matchedSourceName?: string | null;
}

/**
 * The match-source badge shared by every library list row: an emerald "Hardcover" badge when
 * matched, an outline "Unmatched" badge otherwise. Visually identical to the one
 * SeriesListEntry renders inline (series additionally shows a match confidence percentage, which
 * neither a book nor an author match carries - see AudiobookSummaryDto/AuthorSummaryDto's
 * IsMatched doc), extracted here once a second and third consumer (BookListRow, AuthorsList)
 * needed the same treatment.
 */
export function MatchSourceBadge({ isMatched, matchedSourceName }: MatchSourceBadgeProps) {
  return isMatched ? (
    <Badge
      variant="secondary"
      className="gap-1 bg-emerald-500/15 text-[11px] text-emerald-600 dark:text-emerald-400"
    >
      <CheckCircle2 className="h-3 w-3" />
      {matchedSourceName}
    </Badge>
  ) : (
    <Badge variant="outline" className="text-muted-foreground text-[11px]">
      Unmatched
    </Badge>
  );
}

export default MatchSourceBadge;
