import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useServerSuggestions } from "./useServerSuggestions";
import { TYPEAHEAD_RETRY_DELAY_MS } from "@/constants/paging";

// Advances fake timers by `ms` and, wrapped in `act`, flushes the microtasks a fetch's own
// promise settles on - the fetch is triggered by the timer firing, so its resulting setState
// must be applied (and visible to `result.current`) before the next assertion runs.
async function advanceAndFlush(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

describe("useServerSuggestions", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("returns the fetched suggestions after the debounce", async () => {
    const fetchSuggestions = vi.fn().mockResolvedValue(["Alpha", "Beta"]);
    const { result } = renderHook(() => useServerSuggestions("al", fetchSuggestions));

    await advanceAndFlush(150);

    expect(result.current.suggestions).toEqual(["Alpha", "Beta"]);
    expect(result.current.isError).toBe(false);
  });

  it("does not fetch or surface an error for a blank query", async () => {
    const fetchSuggestions = vi.fn().mockResolvedValue(["Alpha"]);
    renderHook(() => useServerSuggestions("", fetchSuggestions));

    await advanceAndFlush(150);

    expect(fetchSuggestions).not.toHaveBeenCalled();
  });

  // A transient failure (a dropped mobile connection, a request that outlives a backgrounded
  // tab's network suspension) must not need the user to retype to get suggestions back.
  it("retries once after a failure and succeeds without surfacing an error", async () => {
    const fetchSuggestions = vi
      .fn()
      .mockRejectedValueOnce(new Error("network down"))
      .mockResolvedValueOnce(["Alpha"]);
    const { result } = renderHook(() => useServerSuggestions("al", fetchSuggestions));

    await advanceAndFlush(150);
    expect(fetchSuggestions).toHaveBeenCalledTimes(1);

    await advanceAndFlush(TYPEAHEAD_RETRY_DELAY_MS);
    expect(fetchSuggestions).toHaveBeenCalledTimes(2);

    expect(result.current.suggestions).toEqual(["Alpha"]);
    expect(result.current.isError).toBe(false);
  });

  it("surfaces an error only once the fetch and its retry both fail", async () => {
    const fetchSuggestions = vi.fn().mockRejectedValue(new Error("network down"));
    const { result } = renderHook(() => useServerSuggestions("al", fetchSuggestions));

    await advanceAndFlush(150);
    expect(fetchSuggestions).toHaveBeenCalledTimes(1);
    // Still within the retry window - not yet reported as an error.
    expect(result.current.isError).toBe(false);

    await advanceAndFlush(TYPEAHEAD_RETRY_DELAY_MS);
    expect(fetchSuggestions).toHaveBeenCalledTimes(2);

    expect(result.current.isError).toBe(true);
    expect(result.current.suggestions).toEqual([]);
  });

  it("clears a stale error once a new query starts", async () => {
    const fetchSuggestions = vi.fn().mockRejectedValue(new Error("network down"));
    const { result, rerender } = renderHook(
      ({ query }) => useServerSuggestions(query, fetchSuggestions),
      { initialProps: { query: "al" } },
    );

    await advanceAndFlush(150 + TYPEAHEAD_RETRY_DELAY_MS);
    expect(result.current.isError).toBe(true);

    fetchSuggestions.mockResolvedValue(["Beta"]);
    rerender({ query: "be" });

    await advanceAndFlush(150);
    expect(result.current.isError).toBe(false);
    expect(result.current.suggestions).toEqual(["Beta"]);
  });

  it("ignores a resolved fetch for a query that is no longer active", async () => {
    let resolveFirst!: (names: string[]) => void;
    const fetchSuggestions = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise<string[]>((resolve) => {
            resolveFirst = resolve;
          }),
      )
      .mockResolvedValueOnce(["Beta"]);

    const { result, rerender } = renderHook(
      ({ query }) => useServerSuggestions(query, fetchSuggestions),
      { initialProps: { query: "al" } },
    );

    await advanceAndFlush(150);
    expect(fetchSuggestions).toHaveBeenCalledTimes(1);

    rerender({ query: "be" });
    await advanceAndFlush(150);
    expect(fetchSuggestions).toHaveBeenCalledTimes(2);
    expect(result.current.suggestions).toEqual(["Beta"]);

    await act(async () => {
      resolveFirst(["Alpha"]);
      await Promise.resolve();
    });
    expect(result.current.suggestions).toEqual(["Beta"]);
  });
});
