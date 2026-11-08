import { describe, it, expect } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useKeptRows } from "./useKeptRows";

interface Item {
  id: number;
  name: string;
}

const key = (item: Item) => item.id;
const a: Item = { id: 1, name: "A" };
const b: Item = { id: 2, name: "B" };
const c: Item = { id: 3, name: "C" };
const d: Item = { id: 4, name: "D" };

function setup(initialItems: Item[], initialResetKey = "page-1") {
  return renderHook(
    ({ items, resetKey }: { items: Item[]; resetKey: string }) =>
      useKeptRows<Item, string>(items, key, resetKey),
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
});
