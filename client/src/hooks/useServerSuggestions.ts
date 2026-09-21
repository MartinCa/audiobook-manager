import { useEffect, useState } from "react";
import { TYPEAHEAD_RETRY_DELAY_MS } from "@/constants/paging";

export interface ServerSuggestionsResult {
  suggestions: string[];
  /** True once the fetch and its one retry have both failed for the current query. */
  isError: boolean;
}

/**
 * Debounced, bounded server-side suggestion fetch for a type-ahead query, shared by
 * TypeaheadInput and TagsInput. A failure is retried once, after TYPEAHEAD_RETRY_DELAY_MS,
 * before giving up - a transient failure (a dropped mobile connection, a request that outlives
 * a backgrounded-tab's network suspension) should not need the user to retype to get
 * suggestions back, and `isError` lets the caller show something better than a dropdown that
 * silently never opens.
 */
export function useServerSuggestions(
  query: string,
  fetchSuggestions: ((query: string) => Promise<string[]>) | undefined,
  debounceMs = 150,
): ServerSuggestionsResult {
  const [suggestions, setSuggestions] = useState<string[]>([]);
  const [isError, setIsError] = useState(false);

  useEffect(() => {
    if (!fetchSuggestions) return;
    let cancelled = false;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;

    const attempt = (isRetry: boolean) => {
      fetchSuggestions(query)
        .then((names) => {
          if (cancelled) return;
          setSuggestions(names);
          setIsError(false);
        })
        .catch(() => {
          if (cancelled) return;
          if (!isRetry) {
            retryTimer = setTimeout(() => attempt(true), TYPEAHEAD_RETRY_DELAY_MS);
          } else {
            setSuggestions([]);
            setIsError(true);
          }
        });
    };

    const timer = setTimeout(() => {
      if (!query) {
        // Nothing to look up; clear the previous lookup and any stale error so a later
        // keystroke cannot resurrect either. Done inside the timer (async), never
        // synchronously in the effect.
        if (!cancelled) {
          setSuggestions([]);
          setIsError(false);
        }
        return;
      }
      // A fresh query starts clean - a failure note from the previous query must not linger
      // over what could well be a successful fetch for this one.
      if (!cancelled) setIsError(false);
      attempt(false);
    }, debounceMs);

    return () => {
      cancelled = true;
      clearTimeout(timer);
      if (retryTimer) clearTimeout(retryTimer);
    };
  }, [query, fetchSuggestions, debounceMs]);

  return { suggestions, isError };
}
