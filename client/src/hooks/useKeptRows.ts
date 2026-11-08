import { useCallback, useMemo, useState } from "react";

type RowKey = string | number;

export interface KeptRow<T, O> {
  item: T;
  key: RowKey;
  /**
   * The server no longer lists this row; it is shown from the snapshot taken when the work
   * started, only so the rows below it stay where they are. Such a row is inert.
   */
  gone: boolean;
  /** How the work on this row ended, if it has. */
  outcome: O | undefined;
}

export interface KeptRows<T, O> {
  /** The list to render: the server's rows, plus kept rows the server has since dropped. */
  rows: KeptRow<T, O>[];
  /** Remember a row (and where it sits) because work on it is starting; clears its old outcome. */
  keep: (item: T) => void;
  /** Forget a row that was kept for work that never started (the request was refused). */
  release: (key: RowKey) => void;
  recordOutcome: (key: RowKey, outcome: O) => void;
  /** Forget every kept row and outcome, e.g. on an explicit reload. */
  clear: () => void;
}

interface State<T, O> {
  resetKey: string;
  entries: { key: RowKey; item: T; index: number }[];
  outcomes: Map<RowKey, O>;
}

const emptyState = <T, O>(resetKey: string): State<T, O> => ({
  resetKey,
  entries: [],
  outcomes: new Map(),
});

/**
 * Keeps a row that has finished (and so left the server's list) where it was until the view
 * changes, so the rows below it do not jump just as the user reaches for the next one.
 *
 * The caller `keep`s a row when it starts work on it. After the server's list refetches without
 * that row it is spliced back in at the position it had, flagged `gone`. State lives only in the
 * component, so navigating away or refreshing the page drops it, and a change of `resetKey` (the
 * page, filters or page size the list is showing) drops it too: a kept row belongs to the view it
 * was in.
 */
export function useKeptRows<T, O>(
  items: readonly T[],
  getKey: (item: T) => RowKey,
  resetKey: string,
): KeptRows<T, O> {
  const [stored, setStored] = useState<State<T, O>>(() => emptyState(resetKey));

  // A different view: start clean (setState during render, as React documents for derived resets).
  let state = stored;
  if (stored.resetKey !== resetKey) {
    state = emptyState(resetKey);
    setStored(state);
  }

  const rows = useMemo(() => {
    const merged: KeptRow<T, O>[] = items.map((item) => {
      const key = getKey(item);
      return { item, key, gone: false, outcome: state.outcomes.get(key) };
    });
    const present = new Set(merged.map((row) => row.key));
    // Ascending, so each splice lands where the row was once the ones above it are back.
    for (const entry of [...state.entries].sort((a, b) => a.index - b.index)) {
      if (present.has(entry.key)) continue;
      merged.splice(Math.min(entry.index, merged.length), 0, {
        item: entry.item,
        key: entry.key,
        gone: true,
        outcome: state.outcomes.get(entry.key),
      });
    }
    return merged;
  }, [items, getKey, state]);

  const keep = useCallback(
    (item: T) => {
      const key = getKey(item);
      setStored((prev) => {
        const base = prev.resetKey === resetKey ? prev : emptyState<T, O>(resetKey);
        const outcomes = new Map(base.outcomes);
        outcomes.delete(key);
        const existing = base.entries.find((entry) => entry.key === key);
        const index =
          existing?.index ??
          Math.max(
            0,
            rows.findIndex((row) => row.key === key),
          );
        return {
          ...base,
          outcomes,
          entries: [...base.entries.filter((entry) => entry.key !== key), { key, item, index }],
        };
      });
    },
    [getKey, resetKey, rows],
  );

  const release = useCallback((key: RowKey) => {
    setStored((prev) => ({
      ...prev,
      entries: prev.entries.filter((entry) => entry.key !== key),
    }));
  }, []);

  const recordOutcome = useCallback(
    (key: RowKey, outcome: O) => {
      setStored((prev) => {
        const base = prev.resetKey === resetKey ? prev : emptyState<T, O>(resetKey);
        return { ...base, outcomes: new Map(base.outcomes).set(key, outcome) };
      });
    },
    [resetKey],
  );

  const clear = useCallback(() => setStored(emptyState(resetKey)), [resetKey]);

  return { rows, keep, release, recordOutcome, clear };
}
