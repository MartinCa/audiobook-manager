import { describe, it, expect } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { mergeKeptRows, useKeptRows, useKeptRowState } from "./useKeptRows";

interface Item {
  id: number;
  name: string;
}

const key = (item: Item) => item.id;
const a: Item = { id: 1, name: "A" };
const b: Item = { id: 2, name: "B" };
const c: Item = { id: 3, name: "C" };
const d: Item = { id: 4, name: "D" };

function setup(
  initialItems: Item[],
  initialResetKey = "page-1",
  shouldHold?: (outcome: string | undefined) => boolean,
) {
  return renderHook(
    ({ items, resetKey }: { items: Item[]; resetKey: string }) =>
      useKeptRows<Item, string>(items, key, resetKey, shouldHold),
    { initialProps: { items: initialItems, resetKey: initialResetKey } },
  );
}

const ids = (rows: { key: string | number }[]) => rows.map((row) => row.key);

describe("useKeptRows", () => {
  it("passes the server's rows straight through when nothing is kept", () => {
    const { result } = setup([a, b, c]);

    expect(ids(result.current.rows)).toEqual([1, 2, 3]);
    expect(result.current.rows.every((row) => !row.gone && row.outcome === undefined)).toBe(true);
  });

  it("puts a kept row back at its position once the server drops it, so the rows below do not shift", () => {
    const { result, rerender } = setup([a, b, c]);

    act(() => result.current.keep(b));
    rerender({ items: [a, c, d], resetKey: "page-1" });

    expect(ids(result.current.rows)).toEqual([1, 2, 3, 4]);
    expect(result.current.rows.map((row) => row.gone)).toEqual([false, true, false, false]);
    expect(result.current.rows[1]?.item).toBe(b);
  });

  it("restores several kept rows each to its own position", () => {
    const { result, rerender } = setup([a, b, c, d]);

    act(() => {
      result.current.keep(a);
      result.current.keep(c);
    });
    rerender({ items: [b, d], resetKey: "page-1" });

    expect(ids(result.current.rows)).toEqual([1, 2, 3, 4]);
    expect(result.current.rows.map((row) => row.gone)).toEqual([true, false, true, false]);
  });

  it("shows the server's own row, not the snapshot, while the server still lists it", () => {
    const { result, rerender } = setup([a, b]);
    const refreshedB = { ...b, name: "B (refreshed)" };

    act(() => result.current.keep(b));
    rerender({ items: [a, refreshedB], resetKey: "page-1" });

    expect(result.current.rows[1]).toMatchObject({ item: refreshedB, gone: false });
  });

  it("records an outcome against a row, and starting new work on that row clears it", () => {
    const { result } = setup([a, b]);

    act(() => result.current.recordOutcome(2, "failed"));
    expect(result.current.rows[1]?.outcome).toBe("failed");

    act(() => result.current.keep(b));
    expect(result.current.rows[1]?.outcome).toBeUndefined();
  });

  it("forgets a row whose work never started", () => {
    const { result, rerender } = setup([a, b, c]);

    act(() => result.current.keep(b));
    act(() => result.current.release(2));
    rerender({ items: [a, c], resetKey: "page-1" });

    expect(ids(result.current.rows)).toEqual([1, 3]);
  });

  it("drops every kept row and outcome when the view changes", () => {
    const { result, rerender } = setup([a, b, c]);
    act(() => {
      result.current.keep(b);
      result.current.recordOutcome(2, "completed");
    });
    rerender({ items: [a, c], resetKey: "page-1" });
    expect(ids(result.current.rows)).toEqual([1, 2, 3]);

    rerender({ items: [a, c], resetKey: "page-2" });

    expect(ids(result.current.rows)).toEqual([1, 3]);
    expect(result.current.rows.every((row) => row.outcome === undefined)).toBe(true);
  });

  it("drops every kept row on an explicit clear", () => {
    const { result, rerender } = setup([a, b, c]);
    act(() => result.current.keep(b));
    rerender({ items: [a, c], resetKey: "page-1" });

    act(() => result.current.clear());

    expect(ids(result.current.rows)).toEqual([1, 3]);
  });

  it("never lets a kept row's position run past the end of a list that has since shrunk", () => {
    const { result, rerender } = setup([a, b, c]);

    act(() => result.current.keep(c));
    rerender({ items: [a], resetKey: "page-1" });

    expect(ids(result.current.rows)).toEqual([1, 3]);
  });

  describe("shouldHold", () => {
    const holdUnlessFailed = (outcome: string | undefined) => outcome !== "failed";

    // Regression: a failed row stays in the server's list because it is still pending. If it then
    // leaves for another reason (rejected, dismissed, removed elsewhere) its kept snapshot used to
    // come back as a placeholder still labelled Failed.
    it("does not resurrect a failed row the server later drops for another reason", () => {
      const { result, rerender } = setup([a, b, c], "page-1", holdUnlessFailed);
      act(() => {
        result.current.keep(b);
        result.current.recordOutcome(2, "failed");
      });
      expect(result.current.rows[1]).toMatchObject({ key: 2, gone: false, outcome: "failed" });

      rerender({ items: [a, c], resetKey: "page-1" });

      expect(ids(result.current.rows)).toEqual([1, 3]);
    });

    it("still holds a row whose work is in flight (no outcome yet) when the server drops it", () => {
      const { result, rerender } = setup([a, b, c], "page-1", holdUnlessFailed);
      act(() => result.current.keep(b));

      rerender({ items: [a, c], resetKey: "page-1" });

      expect(result.current.rows.map((row) => row.gone)).toEqual([false, true, false]);
    });

    it("holds a row that finished successfully when the server drops it", () => {
      const { result, rerender } = setup([a, b, c], "page-1", holdUnlessFailed);
      act(() => {
        result.current.keep(b);
        result.current.recordOutcome(2, "completed");
      });

      rerender({ items: [a, c], resetKey: "page-1" });

      expect(result.current.rows[1]).toMatchObject({ key: 2, gone: true, outcome: "completed" });
    });
  });

  describe("holding", () => {
    const holdUnlessFailed = (outcome: string | undefined) => outcome !== "failed";

    it("is false until something is kept, true while a row is held, false once its work failed", () => {
      const { result } = renderHook(() =>
        useKeptRowState<Item, string>("page-1", holdUnlessFailed),
      );
      expect(result.current.holding).toBe(false);

      act(() => result.current.keepAt(b, 2, 1));
      expect(result.current.holding).toBe(true);

      act(() => result.current.recordOutcome(2, "failed"));
      expect(result.current.holding).toBe(false);
    });

    it("drops back to false when the view changes", () => {
      const { result, rerender } = renderHook(
        ({ resetKey }: { resetKey: string }) => useKeptRowState<Item, string>(resetKey),
        { initialProps: { resetKey: "page-2" } },
      );
      act(() => result.current.keepAt(b, 2, 0));
      expect(result.current.holding).toBe(true);

      rerender({ resetKey: "page-1" });

      expect(result.current.holding).toBe(false);
    });

    it("lets a caller merge the rows after it has used `holding` to pick which page to fetch", () => {
      const { result } = renderHook(() => useKeptRowState<Item, string>("page-2"));
      act(() => result.current.keepAt(b, 2, 0));

      // The page the user is on came back empty because the held row was its only one.
      const rows = mergeKeptRows([], key, result.current);

      expect(rows).toEqual([{ item: b, key: 2, gone: true, outcome: undefined }]);
    });
  });
});
