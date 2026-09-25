import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PendingOnlineMatches } from "./PendingOnlineMatches";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";
import { pendingOnlineMatchApi } from "@/services/api";
import { notifications } from "@/lib/notifications";
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
  render(
    <QueryClientProvider client={queryClient}>
      <RouterTestWrapper ui={<PendingOnlineMatches />} />
    </QueryClientProvider>,
  );
  return { queryClient };
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
  // lower total, since the deletion already happened server-side), and the pager - clamped only
  // for *display* - collapsed to one page and hid itself, with nothing left to click back with.
  // The fetched page must self-correct instead.
  it("self-corrects to the last valid page after rejecting the sole item on the final page, instead of staying permanently empty", async () => {
    // A stateful mock (rather than a fixed sequence of mockResolvedValueOnce calls) because the
    // count query (always page 0) and the items query for whatever page is displayed are two
    // independent queries once the user is past page 0 - both refetch on invalidation, and their
    // relative arrival order isn't guaranteed. Modeling the real backend (101 items, page 2 down
    // to 100 the moment the sole item on it is rejected) keeps the test correct regardless of
    // that order, while still exercising the real self-correction path.
    let rejected = false;
    vi.mocked(pendingOnlineMatchApi.getPendingPage).mockImplementation((page) => {
      const total = rejected ? 100 : 101;
      if (page === 0) {
        return Promise.resolve({
          items: [pendingItem({ audiobookId: 1, bookName: "First" })],
          total,
        });
      }
      if (page === 1) {
        return Promise.resolve(
          rejected
            ? { items: [pendingItem({ audiobookId: 60, bookName: "Still Here" })], total }
            : { items: [pendingItem({ audiobookId: 51, bookName: "Second" })], total },
        );
      }
      // page === 2: the sole item on the final page, gone once rejected (100 items = pages 0-1
      // only, so page 2 no longer exists).
      return Promise.resolve(
        rejected
          ? { items: [], total }
          : { items: [pendingItem({ audiobookId: 101, bookName: "Last Book" })], total },
      );
    });
    vi.mocked(pendingOnlineMatchApi.reject).mockImplementation(() => {
      rejected = true;
      return Promise.resolve();
    });

    renderPage();
    // Wait for page 0 to render, then page to page 2.
    expect(await screen.findByText(/First/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /next/i }));
    expect(await screen.findByText(/Second/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /next/i }));
    expect(await screen.findByText(/Last Book/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /reject/i }));
    await waitFor(() => expect(pendingOnlineMatchApi.reject).toHaveBeenCalledWith(101));

    // Self-corrects to the clamped page (1) automatically - no further click needed - instead of
    // being stuck showing zero rows for a page that no longer exists.
    expect(await screen.findByText(/Still Here/, undefined, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.getByText("Pending (100)")).toBeInTheDocument();
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
});
