import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { act, render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PendingOnlineMatches } from "./PendingOnlineMatches";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";
import { pendingOnlineMatchApi } from "@/services/api";
import { notifications } from "@/lib/notifications";
import { SignalRContext } from "@/context/SignalRContext";
import { SignalREvents, SingleBookOperations } from "@/constants/signalrEvents";
import { createFakeSignalR } from "@/test-utils/fakeSignalR";
import type { PendingOnlineMatchListItem } from "@/types/PendingOnlineMatch";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  pendingOnlineMatchApi: {
    getPendingPage: vi.fn(),
    getFailedPage: vi.fn(),
    reject: vi.fn(),
    dismiss: vi.fn(),
    selectResult: vi.fn(),
  },
  metadataSearchApi: {
    getProxyImageUrl: vi.fn((url: string) => url),
  },
}));

function pendingItem(
  overrides: Partial<PendingOnlineMatchListItem> = {},
): PendingOnlineMatchListItem {
  return {
    audiobookId: 1,
    bookName: "The Way of Kings",
    authors: ["Brandon Sanderson"],
    searchedAt: "2026-01-01T00:00:00Z",
    sourceNames: ["Audible"],
    results: [
      {
        url: "https://www.audible.com/pd/1",
        source: "Audible",
        authors: ["Brandon Sanderson"],
        narrators: [],
        bookName: "The Way of Kings",
        genres: [],
      },
    ],
    ...overrides,
  };
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const hub = createFakeSignalR();
  const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");
  render(
    <SignalRContext.Provider value={hub.value}>
      <QueryClientProvider client={queryClient}>
        <RouterTestWrapper ui={<PendingOnlineMatches />} />
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
  return { queryClient, hub, invalidateSpy };
}

describe("PendingOnlineMatches", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(pendingOnlineMatchApi.getPendingPage).mockResolvedValue({ items: [], total: 0 });
    vi.mocked(pendingOnlineMatchApi.getFailedPage).mockResolvedValue({ items: [], total: 0 });
  });

  it("shows empty states for both lists when nothing is pending or failed", async () => {
    renderPage();

    expect(await screen.findByText(/No pending online matches/)).toBeInTheDocument();
    expect(screen.getByText("No failed or rejected matches.")).toBeInTheDocument();
    expect(screen.getByText("Pending (0)")).toBeInTheDocument();
    expect(screen.getByText("Failed / Rejected (0)")).toBeInTheDocument();
  });

  it("renders a pending row with its book, author and source badges", async () => {
    vi.mocked(pendingOnlineMatchApi.getPendingPage).mockResolvedValue({
      items: [pendingItem()],
      total: 1,
    });

    renderPage();

    expect(await screen.findByText(/The Way of Kings/)).toBeInTheDocument();
    expect(screen.getByText(/Brandon Sanderson/)).toBeInTheDocument();
    expect(screen.getByText("Audible")).toBeInTheDocument();
    expect(screen.getByText("Pending (1)")).toBeInTheDocument();
  });

  it("expands a pending row to show its candidates via the row panel", async () => {
    vi.mocked(pendingOnlineMatchApi.getPendingPage).mockResolvedValue({
      items: [pendingItem()],
      total: 1,
    });

    renderPage();
    const row = await screen.findByText(/The Way of Kings/);

    fireEvent.click(row);

    expect(await screen.findByRole("button", { name: /select this match/i })).toBeInTheDocument();
  });

  it("rejects a pending book, moving it toward the Failed/Rejected list", async () => {
    vi.mocked(pendingOnlineMatchApi.getPendingPage).mockResolvedValue({
      items: [pendingItem()],
      total: 1,
    });
    vi.mocked(pendingOnlineMatchApi.reject).mockResolvedValue(undefined);

    renderPage();
    await screen.findByText(/The Way of Kings/);

    fireEvent.click(screen.getByRole("button", { name: /reject/i }));

    await waitFor(() => {
      expect(pendingOnlineMatchApi.reject).toHaveBeenCalledWith(1);
    });
    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith("Moved to Failed/Rejected");
    });
  });

  it("renders a failed row and dismisses it", async () => {
    vi.mocked(pendingOnlineMatchApi.getFailedPage).mockResolvedValue({
      items: [pendingItem({ audiobookId: 2 })],
      total: 1,
    });
    vi.mocked(pendingOnlineMatchApi.dismiss).mockResolvedValue(undefined);

    renderPage();
    expect(await screen.findByText("Failed / Rejected (1)")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /dismiss/i }));

    await waitFor(() => {
      expect(pendingOnlineMatchApi.dismiss).toHaveBeenCalledWith(2);
    });
    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith("Dismissed");
    });
  });

  it("shows a pager and fetches the next page when the pending list overflows a page", async () => {
    vi.mocked(pendingOnlineMatchApi.getPendingPage)
      .mockResolvedValueOnce({
        items: [pendingItem({ audiobookId: 1, bookName: "First" })],
        total: 125,
      })
      .mockResolvedValueOnce({
        items: [pendingItem({ audiobookId: 51, bookName: "Second" })],
        total: 125,
      });

    renderPage();

    expect(await screen.findByText(/First/)).toBeInTheDocument();
    expect(screen.getByText(/Showing 1–50 of 125/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /next/i }));

    expect(await screen.findByText(/Second/)).toBeInTheDocument();
    expect(pendingOnlineMatchApi.getPendingPage).toHaveBeenLastCalledWith(1, 50);
  });

  // Regression test: rejecting the sole item on the last page used to leave the list
  // permanently empty. The query kept fetching the now out-of-range page (items: [] with a
  // lower total, since the deletion already happened server-side); with the total now fitting in
  // one page, the pager - clamped only for *display* - hides itself entirely (pageCount === 1),
  // leaving nothing to click back with. 51 -> 50 items is the case that actually reproduces this:
  // two pages shrinking to exactly one, not merely three pages shrinking to two (where the pager
  // stays visible and "Previous" alone would have recovered it even pre-fix). The fetched page
  // must self-correct instead.
  it("self-corrects to the last valid page after rejecting the sole item drops the list to a single page, instead of staying permanently empty", async () => {
    // A stateful mock (rather than a fixed sequence of mockResolvedValueOnce calls) because the
    // count query (always page 0) and the items query for whatever page is displayed are two
    // independent queries once the user is past page 0 - both refetch on invalidation, and their
    // relative arrival order isn't guaranteed. Modeling the real backend (51 items, page 1's sole
    // item gone once rejected, dropping to 50 = exactly one page) keeps the test correct
    // regardless of that order, while still exercising the real self-correction path.
    let rejected = false;
    vi.mocked(pendingOnlineMatchApi.getPendingPage).mockImplementation((page) => {
      const total = rejected ? 50 : 51;
      if (page === 0) {
        return Promise.resolve(
          rejected
            ? { items: [pendingItem({ audiobookId: 1, bookName: "Still Here" })], total }
            : { items: [pendingItem({ audiobookId: 1, bookName: "First" })], total },
        );
      }
      // page === 1: the sole item on the final page, gone once rejected (50 items = page 0 only,
      // so page 1 no longer exists - the pager itself disappears, not just "Previous" recovering).
      return Promise.resolve(
        rejected
          ? { items: [], total }
          : { items: [pendingItem({ audiobookId: 51, bookName: "Last Book" })], total },
      );
    });
    vi.mocked(pendingOnlineMatchApi.reject).mockImplementation(() => {
      rejected = true;
      return Promise.resolve();
    });

    renderPage();
    // Wait for page 0 to render, then page to the final page.
    expect(await screen.findByText(/First/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /next/i }));
    expect(await screen.findByText(/Last Book/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /reject/i }));
    await waitFor(() => expect(pendingOnlineMatchApi.reject).toHaveBeenCalledWith(51));

    // Self-corrects to the clamped page (0) automatically - no further click needed, and no
    // pager to click even if there were - instead of being stuck showing zero rows for a page
    // that no longer exists.
    expect(await screen.findByText(/Still Here/, undefined, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.getByText("Pending (50)")).toBeInTheDocument();
  });

  it("shows an error toast when reject fails", async () => {
    vi.mocked(pendingOnlineMatchApi.getPendingPage).mockResolvedValue({
      items: [pendingItem()],
      total: 1,
    });
    vi.mocked(pendingOnlineMatchApi.reject).mockRejectedValue(new Error("Network error"));

    renderPage();
    await screen.findByText(/The Way of Kings/);

    fireEvent.click(screen.getByRole("button", { name: /reject/i }));

    await waitFor(() => {
      expect(notifications.error).toHaveBeenCalled();
    });
  });

  describe("selecting a candidate (runs in the background)", () => {
    async function startSelecting() {
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockResolvedValue({
        items: [pendingItem(), pendingItem({ audiobookId: 2, bookName: "Words of Radiance" })],
        total: 2,
      });
      vi.mocked(pendingOnlineMatchApi.selectResult).mockResolvedValue(undefined);

      const page = renderPage();
      fireEvent.click(await screen.findByText(/The Way of Kings/));
      fireEvent.click(await screen.findByRole("button", { name: /select this match/i }));
      await waitFor(() => expect(pendingOnlineMatchApi.selectResult).toHaveBeenCalledWith(1, 0));
      return page;
    }

    const completion = (overrides: Record<string, unknown> = {}) => ({
      audiobookId: 1,
      operation: SingleBookOperations.pendingMatchSelect,
      status: "completed",
      message: null,
      ...overrides,
    });

    it("collapses the row as soon as the server accepts the pick, so the next book can be worked on", async () => {
      await startSelecting();

      await waitFor(() => {
        expect(
          screen.queryByRole("button", { name: /select this match/i }),
        ).not.toBeInTheDocument();
      });
      expect(screen.getByText("Selecting…")).toBeInTheDocument();
      // Not announced yet: the work is still running, so claiming success now would be a guess.
      expect(notifications.success).not.toHaveBeenCalled();

      // The other book is free to be opened and selected while the first is still working.
      fireEvent.click(screen.getByText(/Words of Radiance/));
      expect(await screen.findByRole("button", { name: /select this match/i })).toBeInTheDocument();
    });

    // Regression: the server starts the work before it returns 202, so a fast failure can reach the
    // page before the POST resolves. The book used to be marked busy only after the POST, which
    // re-added it after the event had cleared it - a row stuck on "Selecting…" until a remount.
    it("does not leave the row stuck busy when the completion arrives before the POST resolves", async () => {
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockResolvedValue({
        items: [pendingItem()],
        total: 1,
      });
      let acceptPost!: () => void;
      vi.mocked(pendingOnlineMatchApi.selectResult).mockReturnValue(
        new Promise<void>((resolve) => {
          acceptPost = resolve;
        }),
      );
      const { hub } = renderPage();
      fireEvent.click(await screen.findByText(/The Way of Kings/));
      fireEvent.click(await screen.findByRole("button", { name: /select this match/i }));
      await screen.findByText("Selecting…");

      act(() =>
        hub.emit(
          SignalREvents.SingleBookOperationComplete,
          completion({ status: "failed", message: "Source unavailable" }),
        ),
      );
      await waitFor(() => expect(screen.queryByText("Selecting…")).not.toBeInTheDocument());
      await act(() => {
        acceptPost();
        return Promise.resolve();
      });

      expect(screen.queryByText("Selecting…")).not.toBeInTheDocument();
      expect(notifications.error).toHaveBeenCalledWith("Source unavailable");
      // The row is free to be reopened and retried.
      fireEvent.click(screen.getByText(/The Way of Kings/));
      expect(await screen.findByRole("button", { name: /select this match/i })).toBeInTheDocument();
    });

    it("keeps the row open and not busy when the server refuses the pick", async () => {
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockResolvedValue({
        items: [pendingItem()],
        total: 1,
      });
      vi.mocked(pendingOnlineMatchApi.selectResult).mockRejectedValue(
        new Error("A match for audiobook 1 is already being selected."),
      );
      renderPage();
      fireEvent.click(await screen.findByText(/The Way of Kings/));
      fireEvent.click(await screen.findByRole("button", { name: /select this match/i }));

      await waitFor(() =>
        expect(notifications.error).toHaveBeenCalledWith(
          "A match for audiobook 1 is already being selected.",
        ),
      );
      expect(screen.queryByText("Selecting…")).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: /select this match/i })).toBeInTheDocument();
    });

    it("keeps a busy row from being reopened or rejected", async () => {
      await startSelecting();
      await screen.findByText("Selecting…");

      fireEvent.click(screen.getByText(/The Way of Kings/));

      expect(screen.queryByRole("button", { name: /select this match/i })).not.toBeInTheDocument();
      const rejectButtons = screen.getAllByRole("button", { name: /reject/i });
      expect(rejectButtons[0]).toBeDisabled();
      expect(rejectButtons[1]).toBeEnabled();
    });

    it("announces success, clears the busy badge and refreshes the views when the work completes", async () => {
      const { hub, invalidateSpy } = await startSelecting();
      await screen.findByText("Selecting…");
      invalidateSpy.mockClear();

      act(() => hub.emit(SignalREvents.SingleBookOperationComplete, completion()));

      await waitFor(() => expect(screen.queryByText("Selecting…")).not.toBeInTheDocument());
      expect(notifications.success).toHaveBeenCalledTimes(1);
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["pendingOnlineMatch"] });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["metadataRefresh"] });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["books"] });
    });

    it("shows the server's message when the work fails, and frees the row again", async () => {
      const { hub } = await startSelecting();
      await screen.findByText("Selecting…");

      act(() =>
        hub.emit(
          SignalREvents.SingleBookOperationComplete,
          completion({ status: "failed", message: "The Hardcover daily request limit is spent." }),
        ),
      );

      await waitFor(() =>
        expect(notifications.error).toHaveBeenCalledWith(
          "The Hardcover daily request limit is spent.",
        ),
      );
      expect(screen.queryByText("Selecting…")).not.toBeInTheDocument();
      expect(notifications.success).not.toHaveBeenCalled();
    });

    // A settled selection leaves the server's list. It must keep its place (and say how it ended)
    // so the rows below it do not jump just as the user reaches for the next one.
    it("keeps a settled row in place, inert and marked, after the list refetches without it", async () => {
      const first = pendingItem();
      const second = pendingItem({ audiobookId: 2, bookName: "Words of Radiance" });
      const third = pendingItem({ audiobookId: 3, bookName: "Oathbringer" });
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockResolvedValue({
        items: [first, second, third],
        total: 3,
      });
      vi.mocked(pendingOnlineMatchApi.selectResult).mockResolvedValue(undefined);
      const { hub } = renderPage();
      fireEvent.click(await screen.findByText(/Words of Radiance/));
      fireEvent.click(await screen.findByRole("button", { name: /select this match/i }));
      await waitFor(() => expect(pendingOnlineMatchApi.selectResult).toHaveBeenCalledWith(2, 0));

      // The server settles the match and deletes the row, so the next fetch leaves it out.
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockResolvedValue({
        items: [first, third],
        total: 2,
      });
      act(() =>
        hub.emit(SignalREvents.SingleBookOperationComplete, completion({ audiobookId: 2 })),
      );

      await waitFor(() => expect(screen.getByText("Pending (2)")).toBeInTheDocument());
      const titles = screen
        .getAllByText(/ — /)
        .map((el) => el.textContent ?? "")
        .map((text) => text.replace(/\s+/g, " "));
      expect(titles).toEqual([
        expect.stringContaining("The Way of Kings"),
        expect.stringContaining("Words of Radiance"),
        expect.stringContaining("Oathbringer"),
      ]);
      expect(screen.getByText("Match selected")).toBeInTheDocument();
      // Nothing to open or reject on the placeholder; its neighbours are unaffected.
      fireEvent.click(screen.getByText(/Words of Radiance/));
      expect(screen.queryByRole("button", { name: /select this match/i })).not.toBeInTheDocument();
      const rejectButtons = screen.getAllByRole("button", { name: /reject/i });
      expect(rejectButtons.map((button) => (button as HTMLButtonElement).disabled)).toEqual([
        false,
        true,
        false,
      ]);
    });

    it("marks a failed row with the reason and keeps it usable for a retry", async () => {
      const { hub } = await startSelecting();
      await screen.findByText("Selecting…");

      act(() =>
        hub.emit(
          SignalREvents.SingleBookOperationComplete,
          completion({ status: "failed", message: "Source unavailable" }),
        ),
      );

      expect(await screen.findByText("Failed")).toBeInTheDocument();
      expect(screen.getByText("Source unavailable")).toBeInTheDocument();
      fireEvent.click(screen.getByText(/The Way of Kings/));
      expect(await screen.findByRole("button", { name: /select this match/i })).toBeInTheDocument();
    });

    // Repro: the user is working on a different, expanded row and has picked another source tab
    // when an earlier selection finishes.
    it("keeps the source tab chosen on another expanded row when a selection finishes", async () => {
      const multiSource = (audiobookId: number, bookName: string) =>
        pendingItem({
          audiobookId,
          bookName,
          sourceNames: ["Audible", "Goodreads"],
          results: [
            {
              url: `https://www.audible.com/pd/${audiobookId}`,
              source: "Audible",
              authors: ["Brandon Sanderson"],
              narrators: [],
              bookName,
              genres: [],
            },
            {
              url: `https://www.goodreads.com/book/${audiobookId}`,
              source: "Goodreads",
              authors: ["Brandon Sanderson"],
              narrators: [],
              bookName: `${bookName} (Goodreads)`,
              genres: [],
            },
          ],
        });
      const list = () => ({
        items: [multiSource(1, "The Way of Kings"), multiSource(2, "Words of Radiance")],
        total: 2,
      });
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockImplementation(() =>
        Promise.resolve(list()),
      );
      vi.mocked(pendingOnlineMatchApi.selectResult).mockResolvedValue(undefined);
      const { hub } = renderPage();
      fireEvent.click(await screen.findByText(/The Way of Kings/));
      fireEvent.click(await screen.findByRole("button", { name: /select this match/i }));
      await waitFor(() => expect(pendingOnlineMatchApi.selectResult).toHaveBeenCalledWith(1, 0));

      // Now working on the second row, on its Goodreads tab.
      fireEvent.click(screen.getByText(/Words of Radiance/));
      await userEvent.click(await screen.findByRole("tab", { name: "Goodreads (1)" }));
      expect(screen.getByText(/Words of Radiance \(Goodreads\)/)).toBeInTheDocument();

      // The first selection settles: the server drops its row and the list refetches.
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockImplementation(() =>
        Promise.resolve({ items: [multiSource(2, "Words of Radiance")], total: 1 }),
      );
      act(() =>
        hub.emit(SignalREvents.SingleBookOperationComplete, completion({ audiobookId: 1 })),
      );
      await screen.findByText("Match selected");
      // "Match selected" comes from the completion event itself, so it does not show the refetched
      // list has reached the expanded row. The pending count dropping from 2 to 1 does: it is read
      // from the same response that rebuilds the row's arrays.
      await waitFor(() => expect(screen.getByText("Pending (1)")).toBeInTheDocument());

      expect(screen.getByText(/Words of Radiance \(Goodreads\)/)).toBeInTheDocument();
    });

    // Regression: with exactly one row on the last page, settling it shrinks the page count. The
    // page actually fetched is clamped to that count, and the clamp used to read as the user
    // leaving the view: the finished row was wiped and the previous page swapped in under the cursor.
    it("keeps the finished row on its own page when settling it removes that last page", async () => {
      const pageOne = Array.from({ length: 50 }, (_, i) =>
        pendingItem({ audiobookId: 100 + i, bookName: `Filler ${i}` }),
      );
      const lonely = pendingItem({ audiobookId: 2, bookName: "Words of Radiance" });
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockImplementation((page: number) =>
        Promise.resolve(
          page === 0 ? { items: pageOne, total: 51 } : { items: [lonely], total: 51 },
        ),
      );
      vi.mocked(pendingOnlineMatchApi.selectResult).mockResolvedValue(undefined);
      const { hub } = renderPage();
      await screen.findByText(/Filler 0/);
      fireEvent.click(screen.getByRole("button", { name: /next/i }));
      fireEvent.click(await screen.findByText(/Words of Radiance/));
      fireEvent.click(await screen.findByRole("button", { name: /select this match/i }));
      await waitFor(() => expect(pendingOnlineMatchApi.selectResult).toHaveBeenCalledWith(2, 0));

      // Settled: the server now has 50 rows, all on the first page.
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockImplementation((page: number) =>
        Promise.resolve(page === 0 ? { items: pageOne, total: 50 } : { items: [], total: 50 }),
      );
      act(() =>
        hub.emit(SignalREvents.SingleBookOperationComplete, completion({ audiobookId: 2 })),
      );

      await waitFor(() => expect(screen.getByText("Pending (50)")).toBeInTheDocument());
      expect(await screen.findByText("Match selected")).toBeInTheDocument();
      expect(screen.getByText(/Words of Radiance/)).toBeInTheDocument();
      // Still the page the user was on: the previous page did not replace it.
      expect(screen.queryByText(/Filler 0/)).not.toBeInTheDocument();
    });

    // Regression: rejecting the only row of the last page clamps the display to the previous page,
    // but the requested page stayed on the vanished one. The next selection made the page "held",
    // which switched the query back to the requested (nonexistent) page and took every other row
    // off the screen with it.
    it("keeps the displayed page when a match is selected after the last page was emptied by a reject", async () => {
      const pageOne = Array.from({ length: 50 }, (_, i) =>
        pendingItem({ audiobookId: 100 + i, bookName: `Filler ${i}` }),
      );
      const lonely = pendingItem({ audiobookId: 2, bookName: "Words of Radiance" });
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockImplementation((page: number) =>
        Promise.resolve(
          page === 0 ? { items: pageOne, total: 51 } : { items: [lonely], total: 51 },
        ),
      );
      vi.mocked(pendingOnlineMatchApi.reject).mockResolvedValue(undefined);
      vi.mocked(pendingOnlineMatchApi.selectResult).mockResolvedValue(undefined);
      renderPage();
      await screen.findByText(/Filler 0/);
      fireEvent.click(screen.getByRole("button", { name: /next/i }));
      await screen.findByText(/Words of Radiance/);
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockImplementation((page: number) =>
        Promise.resolve(page === 0 ? { items: pageOne, total: 50 } : { items: [], total: 50 }),
      );

      fireEvent.click(screen.getByRole("button", { name: /reject/i }));
      await waitFor(() => expect(pendingOnlineMatchApi.reject).toHaveBeenCalledWith(2));
      // The display falls back to the page that still exists...
      fireEvent.click(await screen.findByText(/Filler 0(?!\d)/));
      fireEvent.click(await screen.findByRole("button", { name: /select this match/i }));
      await waitFor(() => expect(pendingOnlineMatchApi.selectResult).toHaveBeenCalledWith(100, 0));
      await screen.findByText("Selecting…");

      // ...and starting work there does not send the query back to the page that is gone.
      expect(screen.getByText(/Filler 7(?!\d)/)).toBeInTheDocument();
      expect(screen.getByText(/Filler 49/)).toBeInTheDocument();
    });

    // Regression: a failed selection keeps its kept snapshot so the row can be retried. Rejecting
    // that row then removes it from Pending - but the snapshot used to come back as a disabled
    // placeholder still labelled Failed, alongside the same book in Failed/Rejected.
    it("does not resurrect a failed row as a placeholder once it has been rejected", async () => {
      const { hub, queryClient } = await startSelecting();
      await screen.findByText("Selecting…");
      act(() =>
        hub.emit(
          SignalREvents.SingleBookOperationComplete,
          completion({ status: "failed", message: "Source unavailable" }),
        ),
      );
      await screen.findByText("Failed");
      vi.mocked(pendingOnlineMatchApi.reject).mockResolvedValue(undefined);
      vi.mocked(pendingOnlineMatchApi.getPendingPage).mockResolvedValue({
        items: [pendingItem({ audiobookId: 2, bookName: "Words of Radiance" })],
        total: 1,
      });

      fireEvent.click(screen.getAllByRole("button", { name: /reject/i })[0]!);
      await waitFor(() => expect(pendingOnlineMatchApi.reject).toHaveBeenCalledWith(1));
      await queryClient.invalidateQueries();

      await waitFor(() => expect(screen.queryByText(/The Way of Kings/)).not.toBeInTheDocument());
      expect(screen.queryByText("Failed")).not.toBeInTheDocument();
      expect(screen.queryByText("Source unavailable")).not.toBeInTheDocument();
      expect(screen.getByText(/Words of Radiance/)).toBeInTheDocument();
    });

    it("ignores another operation's completion for the same book", async () => {
      const { hub } = await startSelecting();
      await screen.findByText("Selecting…");

      act(() =>
        hub.emit(
          SignalREvents.SingleBookOperationComplete,
          completion({ operation: SingleBookOperations.pendingRefreshApply }),
        ),
      );

      expect(screen.getByText("Selecting…")).toBeInTheDocument();
      expect(notifications.success).not.toHaveBeenCalled();
    });
  });
});
