import { describe, it, expect } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useBookSelection } from "./useBookSelection";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";

function book(id: number, bookName = `Book ${id}`): ManagedAudiobook {
  return { id, bookName, authors: [], narrators: [], genres: [] };
}

describe("useBookSelection", () => {
  it("toggles a book into and back out of the selection", () => {
    const { result } = renderHook(() => useBookSelection());

    expect(result.current.count).toBe(0);
    act(() => result.current.toggle(book(1)));
    expect(result.current.count).toBe(1);
    expect(result.current.isSelected(1)).toBe(true);

    act(() => result.current.toggle(book(1)));
    expect(result.current.count).toBe(0);
    expect(result.current.isSelected(1)).toBe(false);
  });

  it("snapshots the title and authors of each selected book", () => {
    const { result } = renderHook(() => useBookSelection());

    act(() => result.current.toggle(book(7, "The Way of Kings")));

    expect(result.current.selectedBooks).toEqual([
      { id: 7, title: "The Way of Kings", authors: [] },
    ]);
  });

  it("selectPage unions with books already picked and never resets the rest", () => {
    const { result } = renderHook(() => useBookSelection());

    act(() => result.current.toggle(book(1)));
    act(() => result.current.selectPage([book(2), book(3)]));

    expect(result.current.count).toBe(3);
    expect([1, 2, 3].every((id) => result.current.isSelected(id))).toBe(true);
  });

  it("deselectPage removes only the books given, keeping the rest", () => {
    const { result } = renderHook(() => useBookSelection());

    act(() => result.current.selectPage([book(1), book(2), book(3)]));
    act(() => result.current.deselectPage([book(1), book(3)]));

    expect(result.current.count).toBe(1);
    expect(result.current.isSelected(2)).toBe(true);
    expect(result.current.isSelected(1)).toBe(false);
  });

  it("reports pageAllSelected only when every book on the page is selected", () => {
    const { result } = renderHook(() => useBookSelection());

    expect(result.current.pageAllSelected([book(1), book(2)])).toBe(false);
    act(() => result.current.selectPage([book(1), book(2)]));
    expect(result.current.pageAllSelected([book(1), book(2)])).toBe(true);
    // A page with no rows is never "all selected".
    expect(result.current.pageAllSelected([])).toBe(false);
    // A book selected on one page says nothing about a different page.
    expect(result.current.pageAllSelected([book(9)])).toBe(false);
  });

  it("reports pageSomeSelected only when some but not all rows are picked", () => {
    const { result } = renderHook(() => useBookSelection());

    expect(result.current.pageSomeSelected([book(1), book(2)])).toBe(false);
    act(() => result.current.toggle(book(1)));
    expect(result.current.pageSomeSelected([book(1), book(2)])).toBe(true);
    act(() => result.current.selectPage([book(1), book(2)]));
    expect(result.current.pageSomeSelected([book(1), book(2)])).toBe(false);
    // Nothing selected on a different page is not "some" of it either.
    expect(result.current.pageSomeSelected([book(9)])).toBe(false);
  });

  it("keeps the selection across a page change within the view - it is never auto-cleared", () => {
    const { result } = renderHook(() => useBookSelection());

    act(() => result.current.toggle(book(1)));
    // Page 2 is a different slice of the library; nothing in the hook resets the picks.
    act(() => result.current.selectPage([book(4), book(5)]));

    expect(result.current.count).toBe(3);
    expect(result.current.isSelected(1)).toBe(true);
    expect(result.current.isSelected(4)).toBe(true);
  });

  it("clear empties the selection", () => {
    const { result } = renderHook(() => useBookSelection());

    act(() => result.current.selectPage([book(1), book(2)]));
    act(() => result.current.clear());

    expect(result.current.count).toBe(0);
    expect(result.current.selectedBooks).toEqual([]);
    expect(result.current.isSelected(1)).toBe(false);
    expect(result.current.pageSomeSelected([book(1), book(2)])).toBe(false);
  });

  it("keeps selection order as the order books entered it", () => {
    const { result } = renderHook(() => useBookSelection());

    act(() => result.current.selectPage([book(3), book(1), book(2)]));
    act(() => result.current.toggle(book(4)));

    expect(result.current.selectedBooks.map((b) => b.id)).toEqual([3, 1, 2, 4]);
  });

  it("re-selecting a deselected book moves it to the end of the selection order", () => {
    const { result } = renderHook(() => useBookSelection());

    act(() => result.current.selectPage([book(1), book(2)]));
    act(() => result.current.toggle(book(1)));
    act(() => result.current.toggle(book(1)));

    expect(result.current.selectedBooks.map((b) => b.id)).toEqual([2, 1]);
  });
});
