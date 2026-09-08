import { describe, it, expect, vi } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useClampedPage } from "./useClampedPage";

describe("useClampedPage", () => {
  it("leaves an in-range page alone", () => {
    const setPage = vi.fn();
    renderHook(() => useClampedPage(1, 3, setPage));

    expect(setPage).not.toHaveBeenCalled();
  });

  it("pulls a page that outran a shrunk total back to the last valid page", () => {
    const setPage = vi.fn();
    renderHook(() => useClampedPage(2, 1, setPage));

    expect(setPage).toHaveBeenCalledWith(0);
  });

  it("re-corrects when the total shrinks again while still on a later page", () => {
    const setPage = vi.fn();
    const { rerender } = renderHook(
      ({ page, pageCount }) => useClampedPage(page, pageCount, setPage),
      { initialProps: { page: 2, pageCount: 3 } },
    );

    rerender({ page: 2, pageCount: 2 });

    expect(setPage).toHaveBeenCalledTimes(1);
    expect(setPage).toHaveBeenLastCalledWith(1);
  });

  it("stops correcting once the page is inside the range again (no loop)", () => {
    const setPage = vi.fn();
    const { rerender } = renderHook(
      ({ page, pageCount }) => useClampedPage(page, pageCount, setPage),
      { initialProps: { page: 5, pageCount: 2 } },
    );

    expect(setPage).toHaveBeenCalledWith(1);

    // Once state lands on the clamped value, the corrected render must not fire again.
    rerender({ page: 1, pageCount: 2 });
    expect(setPage).toHaveBeenCalledTimes(1);

    act(() => {
      setPage.mockClear();
    });
    rerender({ page: 1, pageCount: 2 });
    expect(setPage).not.toHaveBeenCalled();
  });
});
