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

interface Entry<T> {
  key: RowKey;
  item: T;
  index: number;
}

interface State<T, O> {
  resetKey: string;
  entries: Entry<T>[];
  outcomes: Map<RowKey, O>;
}

const emptyState = <T, O>(resetKey: string): State<T, O> => ({
  resetKey,
  entries: [],
  outcomes: new Map(),
});

export interface KeptRowState<T, O> {
  /** Remember a row (and where it sits) because work on it is starting; clears its old outcome. */
  keepAt: (item: T, key: RowKey, index: number) => void;
  /** Forget a row that was kept for work that never started (the request was refused). */
  release: (key: RowKey) => void;
  recordOutcome: (key: RowKey, outcome: O) => void;
  /** Forget every kept row and outcome, e.g. on an explicit reload. */
  clear: () => void;
  /**
   * Whether any kept row would still hold its place. A paged list uses this to stop clamping the
   * page it fetches to the server's page count, which shrinks when the last row of the last page
   * finishes: clamping would swap the view under the cursor, which is the jump this exists to avoid.
   */
  holding: boolean;
  /** @internal for {@link mergeKeptRows}. */
  state: State<T, O>;
  /** @internal for {@link mergeKeptRows}. */
  shouldHold: (outcome: O | undefined) => boolean;
}

const alwaysHold = () => true;

/**
 * The state half of {@link useKeptRows}, separate so a paged list can call it *before* its query
 * (the page to fetch depends on {@link KeptRowState.holding}) and merge afterwards.
 *
 * @param resetKey Identifies the view the kept rows belong to (the requested page, page size,
 *   filters). When it changes the kept rows and outcomes are dropped. It must come from what the
 *   user chose, not from anything the server's counts can change underneath them.
 * @param shouldHold Whether a kept row the server no longer lists should still hold its place,
 *   given its outcome. Defaults to always.
 */
export function useKeptRowState<T, O>(
  resetKey: string,
  shouldHold: (outcome: O | undefined) => boolean = alwaysHold,
): KeptRowState<T, O> {
  const [stored, setStored] = useState<State<T, O>>(() => emptyState(resetKey));

  // A different view: start clean (setState during render, as React documents for derived resets).
  let state = stored;
  if (stored.resetKey !== resetKey) {
    state = emptyState(resetKey);
    setStored(state);
  }

  const keepAt = useCallback(
    (item: T, key: RowKey, index: number) => {
      setStored((prev) => {
        const base = prev.resetKey === resetKey ? prev : emptyState<T, O>(resetKey);
        const outcomes = new Map(base.outcomes);
        outcomes.delete(key);
        const existing = base.entries.find((entry) => entry.key === key);
        return {
          ...base,
          outcomes,
          entries: [
            ...base.entries.filter((entry) => entry.key !== key),
            { key, item, index: existing?.index ?? Math.max(0, index) },
          ],
        };
      });
    },
    [resetKey],
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

  const holding = state.entries.some((entry) => shouldHold(state.outcomes.get(entry.key)));

  return { keepAt, release, recordOutcome, clear, holding, state, shouldHold };
}

/**
 * The server's rows with the kept rows it has dropped spliced back in at the position they had.
 * A kept row the server still lists shows the server's own data. A kept row the server no longer
 * lists is shown (flagged `gone`) only while its outcome says it should hold its place.
 */
export function mergeKeptRows<T, O>(
  items: readonly T[],
  getKey: (item: T) => RowKey,
  kept: Pick<KeptRowState<T, O>, "state" | "shouldHold">,
): KeptRow<T, O>[] {
  const { state, shouldHold } = kept;
  const merged: KeptRow<T, O>[] = items.map((item) => {
    const key = getKey(item);
    return { item, key, gone: false, outcome: state.outcomes.get(key) };
  });
  const present = new Set(merged.map((row) => row.key));
  // Ascending, so each splice lands where the row was once the ones above it are back.
  for (const entry of [...state.entries].sort((a, b) => a.index - b.index)) {
    if (present.has(entry.key)) continue;
    const outcome = state.outcomes.get(entry.key);
    if (!shouldHold(outcome)) continue;
    merged.splice(Math.min(entry.index, merged.length), 0, {
      item: entry.item,
      key: entry.key,
      gone: true,
      outcome,
    });
  }
  return merged;
}

export interface KeptRows<T, O> extends Omit<
  KeptRowState<T, O>,
  "keepAt" | "state" | "shouldHold"
> {
  /** The list to render: the server's rows, plus kept rows the server has since dropped. */
  rows: KeptRow<T, O>[];
  /** Remember a row (at the position it has in `rows`) because work on it is starting. */
  keep: (item: T) => void;
}

/**
 * Keeps a row that has finished (and so left the server's list) where it was until the view
 * changes, so the rows below it do not jump just as the user reaches for the next one.
 *
 * The caller `keep`s a row when it starts work on it. After the server's list refetches without
 * that row it is spliced back in at the position it had, flagged `gone`. State lives only in the
 * component, so navigating away or refreshing the page drops it, and a change of `resetKey` (the
 * page, filters or page size the list is showing) drops it too: a kept row belongs to the view it
 * was in. For a paged list whose page depends on {@link KeptRowState.holding}, use
 * {@link useKeptRowState} and {@link mergeKeptRows} directly.
 */
export function useKeptRows<T, O>(
  items: readonly T[],
  getKey: (item: T) => RowKey,
  resetKey: string,
  shouldHold?: (outcome: O | undefined) => boolean,
): KeptRows<T, O> {
  const kept = useKeptRowState<T, O>(resetKey, shouldHold);
  const rows = useMemo(() => mergeKeptRows(items, getKey, kept), [items, getKey, kept]);

  const { keepAt } = kept;
  const keep = useCallback(
    (item: T) => {
      const key = getKey(item);
      keepAt(
        item,
        key,
        rows.findIndex((row) => row.key === key),
      );
    },
    [getKey, keepAt, rows],
  );

  return {
    rows,
    keep,
    release: kept.release,
    recordOutcome: kept.recordOutcome,
    clear: kept.clear,
    holding: kept.holding,
  };
}
