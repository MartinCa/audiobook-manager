import { describe, it, expect, vi, beforeEach } from "vitest";
import { act, render, screen, fireEvent, waitFor } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";
import { SignalREvents, SingleBookOperations } from "@/constants/signalrEvents";
import { createFakeSignalR } from "@/test-utils/fakeSignalR";
import type { PendingMetadataRefreshListItem } from "@/types/MetadataRefresh";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

// The real panel needs the book, the snapshot, languages and the apply rules; what is under test
// here is how the page treats a row once the panel has started the work, so the panel is reduced
// to the one call that matters: handing its request to the page's `start`.
const applyRequest = vi.fn<() => Promise<void>>();
vi.mock("./library/PendingRefreshRowPanel", () => ({
  PendingRefreshRowPanel: ({
    audiobookId,
    start,
  }: {
    audiobookId: number;
    start: (request: () => Promise<void>) => Promise<void>;
  }) => (
    <button
      type="button"
      onClick={() => {
        void start(applyRequest).catch(() => undefined);
      }}
    >
      Apply book {audiobookId}
    </button>
  ),
}));

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    metadataRefreshApi: {
      getPendingPage: vi.fn(),
      getPendingSummary: vi.fn().mockResolvedValue([]),
      getPendingForAudiobook: vi.fn(),
      reevaluatePending: vi.fn(),
    },
    metadataSearchApi: {
      getServices: vi.fn().mockResolvedValue([{ name: "Goodreads", enabled: true }]),
    },
    operationsApi: {
      getStatus: vi.fn().mockResolvedValue({ isRunning: false }),
    },
  };
});

import type * as ApiModule from "@/services/api";
import { metadataRefreshApi } from "@/services/api";

const item = (audiobookId: number, bookName: string): PendingMetadataRefreshListItem => ({
  audiobookId,
  bookName,
  authors: ["Brandon Sanderson"],
  fetchedAt: "2026-09-01T12:00:00Z",
  sourceName: "Goodreads",
  changedFields: ["Rating"],
});

const first = item(1, "The Way of Kings");
const second = item(2, "Words of Radiance");
const third = item(3, "Oathbringer");

describe("MetadataRefresh: rows that finished applying", () => {
  let hub: ReturnType<typeof createFakeSignalR>;

  beforeEach(() => {
    vi.clearAllMocks();
    hub = createFakeSignalR();
    applyRequest.mockResolvedValue(undefined);
    vi.mocked(metadataRefreshApi.getPendingPage).mockResolvedValue({
      items: [first, second, third],
      total: 3,
    });
  });

  function renderPage() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const history = createMemoryHistory({ initialEntries: ["/library/metadata-refresh"] });
    const router = createRouter({ routeTree, history });
    render(
      <ThemeProvider defaultTheme="system" storageKey="theme">
        <SignalRContext.Provider value={hub.value}>
          <QueryClientProvider client={queryClient}>
            <RouterProvider router={router} />
          </QueryClientProvider>
        </SignalRContext.Provider>
      </ThemeProvider>,
    );
  }

  const completion = (
    audiobookId: number,
    status = "completed",
    message: string | null = null,
  ) => ({
    audiobookId,
    operation: SingleBookOperations.pendingRefreshApply,
    status,
    message,
  });

  const titles = () =>
    screen
      .getAllByText(/ — /)
      .map((el) => (el.textContent ?? "").replace(/\s+/g, " "))
      .map((text) => text.split(" — ")[1] ?? text);

  async function startApplyingSecond() {
    renderPage();
    fireEvent.click(await screen.findByText(/Words of Radiance/));
    fireEvent.click(await screen.findByRole("button", { name: "Apply book 2" }));
    await waitFor(() => expect(applyRequest).toHaveBeenCalledTimes(1));
    await screen.findByText("Applying…");
  }

  it("keeps an applied row in place, marked Applied and inert, after the list refetches without it", async () => {
    await startApplyingSecond();
    vi.mocked(metadataRefreshApi.getPendingPage).mockResolvedValue({
      items: [first, third],
      total: 2,
    });

    act(() => hub.emit(SignalREvents.SingleBookOperationComplete, completion(2)));

    await waitFor(() => expect(screen.getByText("Applied")).toBeInTheDocument());
    await waitFor(() => expect(metadataRefreshApi.getPendingPage).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByText("Applying…")).not.toBeInTheDocument());
    expect(titles()).toEqual([
      expect.stringContaining("The Way of Kings"),
      expect.stringContaining("Words of Radiance"),
      expect.stringContaining("Oathbringer"),
    ]);
    // Nothing to open or select on the placeholder; its neighbours are unaffected.
    fireEvent.click(screen.getByText(/Words of Radiance/));
    expect(screen.queryByRole("button", { name: "Apply book 2" })).not.toBeInTheDocument();
    const checkboxes = screen.getAllByRole("checkbox").slice(1);
    expect(checkboxes.map((box) => box.hasAttribute("data-disabled"))).toEqual([
      false,
      true,
      false,
    ]);
  });

  it("does not offer a placeholder row to a bulk action", async () => {
    // Selecting the row first, then applying it, must drop it from the selection.
    renderPage();
    await screen.findByText(/Words of Radiance/);
    fireEvent.click(screen.getAllByRole("checkbox")[2]!);
    expect(await screen.findByText("1 book(s) selected")).toBeInTheDocument();
    fireEvent.click(screen.getByText(/Words of Radiance/));
    fireEvent.click(await screen.findByRole("button", { name: "Apply book 2" }));
    await waitFor(() => expect(applyRequest).toHaveBeenCalledTimes(1));

    await waitFor(() => expect(screen.queryByText("1 book(s) selected")).not.toBeInTheDocument());
  });

  it("keeps the list, not the empty state, when the last pending row has been applied", async () => {
    vi.mocked(metadataRefreshApi.getPendingPage).mockResolvedValue({ items: [second], total: 1 });
    renderPage();
    fireEvent.click(await screen.findByText(/Words of Radiance/));
    fireEvent.click(await screen.findByRole("button", { name: "Apply book 2" }));
    await screen.findByText("Applying…");
    vi.mocked(metadataRefreshApi.getPendingPage).mockResolvedValue({ items: [], total: 0 });

    act(() => hub.emit(SignalREvents.SingleBookOperationComplete, completion(2)));

    await waitFor(() => expect(metadataRefreshApi.getPendingPage).toHaveBeenCalledTimes(2));
    expect(await screen.findByText("Applied")).toBeInTheDocument();
    expect(screen.queryByText("No pending metadata changes")).not.toBeInTheDocument();
  });

  it("marks a failed apply with its reason and leaves the row usable for a retry", async () => {
    await startApplyingSecond();

    act(() =>
      hub.emit(
        SignalREvents.SingleBookOperationComplete,
        completion(2, "failed", "Another operation is already modifying audiobook 2."),
      ),
    );

    expect(await screen.findByText("Failed")).toBeInTheDocument();
    expect(
      screen.getByText("Another operation is already modifying audiobook 2."),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByText(/Words of Radiance/));
    expect(await screen.findByRole("button", { name: "Apply book 2" })).toBeInTheDocument();
  });

  it("forgets the kept row when the request is refused, since nothing started", async () => {
    applyRequest.mockRejectedValue(new Error("409"));
    renderPage();
    fireEvent.click(await screen.findByText(/Words of Radiance/));
    fireEvent.click(await screen.findByRole("button", { name: "Apply book 2" }));
    await waitFor(() => expect(applyRequest).toHaveBeenCalledTimes(1));
    vi.mocked(metadataRefreshApi.getPendingPage).mockResolvedValue({
      items: [first, third],
      total: 2,
    });

    // A later refetch (here: the user pressing the page's own invalidation path via an event for
    // another book) must not resurrect a row that never started.
    act(() => hub.emit(SignalREvents.SingleBookOperationComplete, completion(1)));

    await waitFor(() => expect(metadataRefreshApi.getPendingPage).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByText(/Words of Radiance/)).not.toBeInTheDocument());
  });
});
