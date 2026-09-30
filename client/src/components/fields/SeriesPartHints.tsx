import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Info } from "lucide-react";
import { audiobookApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";

export interface SeriesPartHintsProps {
  series: string;
  part: string;
  /** The book's qualifier keys: only books carrying the same set can conflict on a part. */
  qualifiers: readonly string[];
  /** The book being edited; without one (the organize flow) no conflict check runs. */
  currentBookId?: number;
}

/**
 * The advisory notes under one series + part pair: "series is set but has no part", and whether
 * another book already uses this part in this series. Server-backed and bounded (per-book,
 * part-equivalence applied server-side, capped result) and debounced, so a keystroke in either
 * field does not fire one request per character. Never blocks a save - a heads-up only. One
 * instance per series row, because the conflict question is asked per (series, part).
 */
export function SeriesPartHints({ series, part, qualifiers, currentBookId }: SeriesPartHintsProps) {
  const seriesValue = series.trim();
  const partValue = part.trim();
  const [debouncedSeries, setDebouncedSeries] = useState(seriesValue);
  const [debouncedPart, setDebouncedPart] = useState(partValue);
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSeries(seriesValue), 300);
    return () => clearTimeout(timer);
  }, [seriesValue]);
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedPart(partValue), 300);
    return () => clearTimeout(timer);
  }, [partValue]);

  const seriesSetWithNoPart = seriesValue.length > 0 && partValue.length === 0;
  // Joined into a string so the query key (and the request) stay stable across renders.
  const qualifiersKey = qualifiers.join(",");

  const { data: conflictCheck, isError: conflictError } = useQuery({
    queryKey: queryKeys.seriesPartConflicts(
      currentBookId,
      debouncedSeries,
      debouncedPart,
      qualifiersKey,
    ),
    queryFn: () =>
      audiobookApi.getSeriesPartConflicts(
        currentBookId!,
        debouncedSeries,
        debouncedPart,
        qualifiersKey ? qualifiersKey.split(",") : [],
      ),
    enabled: currentBookId !== undefined && debouncedSeries.length > 0 && debouncedPart.length > 0,
  });
  const conflicts = conflictCheck?.conflicts ?? [];
  const truncated = conflictCheck?.truncated ?? false;

  return (
    <>
      {seriesSetWithNoPart && (
        <p className="text-status-unknown mt-1 flex items-center gap-1 text-xs">
          <Info className="h-3 w-3 shrink-0" />
          <span>
            Series is set but no series part is entered — the book will have no position within it.
          </span>
        </p>
      )}

      {conflictError && (
        <p role="alert" className="text-status-error mt-1 flex items-center gap-1 text-xs">
          <AlertTriangle className="h-3 w-3 shrink-0" />
          <span>Couldn't check for series-part conflicts — saving is still allowed.</span>
        </p>
      )}

      {conflicts.length > 0 && (
        <div className="rounded-md border border-amber-500/20 bg-amber-500/10 p-2.5 text-xs text-amber-900 dark:text-amber-300">
          <div className="flex items-center gap-1.5 font-semibold">
            <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
            <span>Another book already uses this series part</span>
          </div>
          <ul className="mt-1 list-inside list-disc space-y-0.5">
            {conflicts.map((conflict) => (
              <li key={conflict.audiobookId}>
                <a
                  href={`/library/book/${conflict.audiobookId}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="font-medium break-all underline"
                >
                  {conflict.bookName}
                  {conflict.seriesPart ? ` · part ${conflict.seriesPart}` : ""}
                </a>
              </li>
            ))}
          </ul>
          {truncated && (
            <p className="mt-1 opacity-80">
              List is truncated — more books share this series part than are shown.
            </p>
          )}
          <p className="mt-1 opacity-80">
            Saving is still allowed — this is a heads-up, not a blocker.
          </p>
        </div>
      )}
    </>
  );
}
