import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { similarValuesApi } from "@/services/api";
import type { EntryStatus } from "@/types/EntryStatus";

export type EntryValueType = "author" | "narrator" | "series";

export interface EntryStatusResult {
  /** The server's classification, or null while loading / blank / errored. */
  status: EntryStatus | null;
  /**
   * True when the bounded classification query failed. The caller must surface this rather than
   * silently treating the entry as "new" - an indicator that cannot reach the server must not
   * claim certainty either way.
   */
  isError: boolean;
}

/**
 * Bounded server-backed classification of one typed author/narrator/series entry into
 * exact-existing / similar / new. Debounced so a keystroke does not fire one request per
 * character, cached by value via TanStack Query, and never queried for a blank value. A
 * passed-in value can update at any point (typing, a metadata-search apply), so the indicator
 * is recomputed for whatever the entry currently holds.
 */
export function useEntryStatus(valueType: EntryValueType, value: string): EntryStatusResult {
  const [debounced, setDebounced] = useState(value.trim());

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value.trim()), 250);
    return () => clearTimeout(timer);
  }, [value]);

  const { data, isError } = useQuery({
    queryKey: ["entryStatus", valueType, debounced],
    queryFn: () => similarValuesApi.getEntryStatus(valueType, debounced, 3),
    enabled: debounced.length > 0,
    staleTime: 30_000,
  });

  return { status: data ?? null, isError };
}
