import { describe, it, expect, vi, beforeEach } from "vitest";
import { act, render, screen, fireEvent, waitFor } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { SignalREvents, SingleBookOperations } from "@/constants/signalrEvents";
import { createFakeSignalR } from "@/test-utils/fakeSignalR";
import { ThemeProvider } from "@/components/theme-provider";
import { notifications } from "@/lib/notifications";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    metadataRefreshApi: {
      refreshAudiobook: vi.fn(),
      startBulkRefresh: vi.fn().mockResolvedValue(undefined),
      getPendingPage: vi.fn(),
      getPendingSummary: vi.fn().mockResolvedValue([]),
      getPendingForAudiobook: vi.fn(),
      dismissPending: vi.fn().mockResolvedValue(undefined),
      dismissSelected: vi.fn().mockResolvedValue({ dismissed: 0 }),
      applyPending: vi.fn().mockResolvedValue(undefined),
      applySelected: vi.fn().mockResolvedValue(undefined),
      applyFiltered: vi.fn().mockResolvedValue(undefined),
      reevaluatePending: vi.fn(),
    },
    metadataSearchApi: {
      getServices: vi.fn().mockResolvedValue([
        { name: "Goodreads", enabled: true },
        { name: "Audible", enabled: true },
      ]),
    },
    operationsApi: {
      getStatus: vi.fn().mockResolvedValue({ isRunning: false }),
    },
  };
});

import type * as ApiModule from "@/services/api";
import { metadataRefreshApi } from "@/services/api";
import { cutoffDateToUtcIso } from "@/helpers/metadataRefresh";
import type { PendingMetadataRefreshListItem } from "@/types/MetadataRefresh";

describe("MetadataRefresh", () => {
  let queryClient: QueryClient;

  let hub: ReturnType<typeof createFakeSignalR>;

  const sampleItems: PendingMetadataRefreshListItem[] = [
    {
      audiobookId: 1,
      bookName: "The Way of Kings",
      authors: ["Brandon Sanderson"],
      fetchedAt: "2026-09-01T12:00:00Z",
      sourceName: "Goodreads",
      changedFields: ["Rating"],
    },
    {
      audiobookId: 2,
      bookName: "Words of Radiance",
      authors: ["Brandon Sanderson"],
      fetchedAt: "2026-09-01T12:05:00Z",
      sourceName: "Goodreads",
      changedFields: ["Publisher"],
    },
  ];

  beforeEach(() => {
    vi.clearAllMocks();
    hub = createFakeSignalR();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    vi.mocked(metadataRefreshApi.getPendingPage).mockResolvedValue({
      items: sampleItems,
      total: sampleItems.length,
    });
  });

  function renderWithRouter(initialEntry = "/library/metadata-refresh") {
    const history = createMemoryHistory({ initialEntries: [initialEntry] });
    const router = createRouter({ routeTree, history });

    const result = render(
      <ThemeProvider defaultTheme="system" storageKey="theme">
        <SignalRContext.Provider value={hub.value}>
          <QueryClientProvider client={queryClient}>
            <RouterProvider router={router} />
          </QueryClientProvider>
        </SignalRContext.Provider>
      </ThemeProvider>,
    );

    return { ...result, router };
  }

  it("renders the page and lists pending items", async () => {
    renderWithRouter();

    expect(await screen.findByText("Metadata Refresh")).toBeInTheDocument();
    expect(await screen.findByText("Books with Pending Metadata Changes (2)")).toBeInTheDocument();

    expect(screen.getByText(/Brandon Sanderson — The Way of Kings/)).toBeInTheDocument();
    expect(screen.getByText(/Brandon Sanderson — Words of Radiance/)).toBeInTheDocument();
    expect(screen.getAllByText("Goodreads")).toHaveLength(2);
  });

  it("stacks the row on phones: title wrapper and actions wrapper collapse from sm: up", async () => {
    renderWithRouter();

    await screen.findByText("Books with Pending Metadata Changes (2)");

    // Scoped to the first row, found through its source badge.
    const actions = screen.getAllByText("Goodreads")[0]!.parentElement!;
    const row = actions.parentElement!;
    const titleWrapper = row.firstElementChild!;

    // The row stacks below sm: and is one line from sm: up.
    expect(row).toHaveClass("flex-col", "sm:flex-row");
    // Checkbox + title on top, the source badge and View in a second row; both wrappers vanish
    // from sm: up so their children are the row's own flex items again.
    expect(titleWrapper).toHaveClass("sm:contents");
    expect(titleWrapper).toContainElement(screen.getAllByRole("checkbox")[1]!);
    expect(titleWrapper).toContainElement(screen.getAllByText(/Brandon Sanderson/)[0]!);
    expect(actions).toHaveClass("sm:contents");
    expect(actions).toContainElement(screen.getAllByRole("link", { name: "View" })[0]!);
    // The "Pending since" timestamp is dropped on phones: it only wraps the title column away.
    expect(screen.getAllByText(/^Pending since /)[0]).toHaveClass("max-sm:hidden");
  });

  it("shows the empty state when nothing is pending", async () => {
    vi.mocked(metadataRefreshApi.getPendingPage).mockResolvedValue({
      items: [],
      total: 0,
    });

    renderWithRouter();

    expect(await screen.findByText("No pending metadata changes")).toBeInTheDocument();
  });

  it("starts a bulk refresh with no cutoff and shows a success toast", async () => {
    vi.mocked(metadataRefreshApi.startBulkRefresh).mockResolvedValueOnce(undefined);

    renderWithRouter();
    const refreshButton = await screen.findByRole("button", { name: /refresh books/i });
    fireEvent.click(refreshButton);

    await waitFor(() => {
      expect(metadataRefreshApi.startBulkRefresh).toHaveBeenCalledWith(undefined);
    });
    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith("Metadata refresh started in background");
    });
  });

  it("passes a UTC cut-off when a date is chosen", async () => {
    renderWithRouter();

    const dateInput = await screen.findByLabelText(
      /refresh books last refreshed before this date/i,
    );
    fireEvent.change(dateInput, {
      target: { value: "2026-09-01" },
    });
    const refreshButton = await screen.findByRole("button", { name: /refresh books/i });
    fireEvent.click(refreshButton);

    await waitFor(() => {
      expect(metadataRefreshApi.startBulkRefresh).toHaveBeenCalled();
    });
    const arg = vi.mocked(metadataRefreshApi.startBulkRefresh).mock.calls[0]![0];
    // `cutoffDateToUtcIso` builds `new Date("YYYY-MM-DDT00:00:00")`, i.e. local midnight, then
    // converts to UTC. We round-trip that instant back through the *local* calendar because the
    // user's chosen date is a local day: a UTC-day comparison only equals the chosen date when
    // the runner itself is in UTC (which is why CI never caught it) — in any TZ ahead of UTC the
    // UTC day is local-midnight minus the offset, i.e. the previous day.
    expect(cutoffDateToUtcIso("2026-09-01")).toBe(arg);
    const roundTrip = new Date(arg!);
    expect(roundTrip.getFullYear()).toBe(2026);
    expect(roundTrip.getMonth()).toBe(8); // September (0-based)
    expect(roundTrip.getDate()).toBe(1);
    expect(roundTrip.getHours()).toBe(0);
    expect(roundTrip.getMinutes()).toBe(0);
  });

  it("converts the cut-off date to a UTC ISO timestamp at the user's local midnight", () => {
    // `cutoffDateToUtcIso` builds `new Date("YYYY-MM-DDT00:00:00")`, i.e. local midnight, then
    // converts to UTC. Whatever the runner's TZ, the UTC day is now local-midnight minus the
    // offset, so only the day-level invariant is asserted here (TZ-independent) plus that an
    // empty input stays empty.
    expect(cutoffDateToUtcIso("")).toBeUndefined();
    const utc = cutoffDateToUtcIso("2026-09-01")!;
    expect(Number.isNaN(Date.parse(utc))).toBe(false);
    const local = new Date("2026-09-01T00:00:00");
    expect(utc).toBe(local.toISOString());
  });

  it("paginates when the result set spans multiple pages", async () => {
    vi.mocked(metadataRefreshApi.getPendingPage).mockResolvedValue({
      items: sampleItems,
      total: 51,
    });

    renderWithRouter();

    await waitFor(() => {
      expect(screen.getByText(/Showing 1–50 of 51/)).toBeInTheDocument();
    });
    const next = await screen.findByRole("button", { name: /next/i });
    fireEvent.click(next);

    await waitFor(() => {
      expect(metadataRefreshApi.getPendingPage).toHaveBeenCalledWith(1, 50, [], []);
    });
    const prev = screen.getByRole("button", { name: /previous/i });
    expect(prev).not.toBeDisabled();
    expect(screen.getByText(/Showing 51–51 of 51/)).toBeInTheDocument();
  });

  it("filters by source and re-queries with the selected source", async () => {
    renderWithRouter();

    await screen.findByText("Books with Pending Metadata Changes (2)");

    const sourceFilterButton = await screen.findByRole("button", { name: /filter sources/i });
    fireEvent.click(sourceFilterButton);
    const audibleOption = await screen.findByText("Audible");
    fireEvent.click(audibleOption);

    await waitFor(() => {
      expect(metadataRefreshApi.getPendingPage).toHaveBeenCalledWith(0, 50, [], ["Audible"]);
    });
  });

  // Regression: "apply all matching filter" used to send only the field filter, so it applied
  // books from every source while the list (and its count) showed just the filtered source.
  it("applies all matching the active source filter, sending the selected sources", async () => {
    renderWithRouter();

    await screen.findByText("Books with Pending Metadata Changes (2)");

    fireEvent.click(await screen.findByRole("button", { name: /filter sources/i }));
    fireEvent.click(await screen.findByText("Audible"));

    const applyAll = await screen.findByRole("button", { name: /apply all matching filter/i });
    fireEvent.click(applyAll);

    await waitFor(() => {
      expect(metadataRefreshApi.applyFiltered).toHaveBeenCalledWith([], false, ["Audible"]);
    });
  });

  it("hides apply-all-matching-filter while no filter is active", async () => {
    renderWithRouter();

    await screen.findByText("Books with Pending Metadata Changes (2)");

    expect(screen.queryByRole("button", { name: /apply all matching filter/i })).toBeNull();
  });

  it("dismisses the selected books without applying them", async () => {
    vi.mocked(metadataRefreshApi.dismissSelected).mockResolvedValueOnce({ dismissed: 1 });

    renderWithRouter();

    await screen.findByText(/Brandon Sanderson — The Way of Kings/);
    const checkboxes = screen.getAllByRole("checkbox");
    // First checkbox is "select shown"; the row checkboxes follow.
    fireEvent.click(checkboxes[1]!);

    const dismissButton = await screen.findByRole("button", { name: /dismiss selected \(1\)/i });
    fireEvent.click(dismissButton);

    await waitFor(() => {
      expect(metadataRefreshApi.dismissSelected).toHaveBeenCalledWith([1]);
    });
    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith("Dismissed 1 pending change(s)");
    });
  });

  it("re-evaluating announces the background apply only when the server started one", async () => {
    vi.mocked(metadataRefreshApi.reevaluatePending)
      .mockResolvedValueOnce({ processed: 3, updated: 1, removed: 0, autoApplyStarted: true })
      .mockResolvedValueOnce({ processed: 3, updated: 0, removed: 0, autoApplyStarted: false });
    renderWithRouter();

    await screen.findByText(/Brandon Sanderson — The Way of Kings/);
    fireEvent.click(screen.getByRole("button", { name: /re-evaluate pending changes/i }));
    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith(
        "Re-evaluated 3 pending change(s): 1 updated, 0 resolved. Applying changes the automated rules settle…",
      );
    });

    fireEvent.click(screen.getByRole("button", { name: /re-evaluate pending changes/i }));
    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith(
        "Re-evaluated 3 pending change(s): 0 updated, 0 resolved",
      );
    });
  });

  // Regression: the split-on-colon toggle defaults off and must be threaded through to
  // applySelected as an explicit false, not omitted.
  it("applies selected books with splitTitleOnColon=false by default", async () => {
    renderWithRouter();

    await screen.findByText(/Brandon Sanderson — The Way of Kings/);
    const checkboxes = screen.getAllByRole("checkbox");
    // First checkbox is "select shown"; the row checkboxes follow.
    fireEvent.click(checkboxes[1]!);

    const applyButton = await screen.findByRole("button", { name: /apply selected \(1\)/i });
    fireEvent.click(applyButton);

    await waitFor(() => {
      expect(metadataRefreshApi.applySelected).toHaveBeenCalledWith([1], false);
    });
  });

  // Checking the page-level toggle passes splitTitleOnColon=true through to applySelected.
  it("applies selected books with splitTitleOnColon=true once the toggle is checked", async () => {
    renderWithRouter();

    await screen.findByText(/Brandon Sanderson — The Way of Kings/);
    const checkboxes = screen.getAllByRole("checkbox");
    fireEvent.click(checkboxes[1]!);

    const splitToggle = await screen.findByRole("checkbox", {
      name: "Split titles into book name and subtitle at first colon when applying",
    });
    fireEvent.click(splitToggle);

    const applyButton = await screen.findByRole("button", { name: /apply selected \(1\)/i });
    fireEvent.click(applyButton);

    await waitFor(() => {
      expect(metadataRefreshApi.applySelected).toHaveBeenCalledWith([1], true);
    });
  });

  // A single row's quick apply runs in the background, and its row panel has unmounted by the time
  // the server reports back, so the page is what announces the outcome.
  describe("single-row apply outcome", () => {
    const completion = (status: string, message: string | null = null) => ({
      audiobookId: 1,
      operation: SingleBookOperations.pendingRefreshApply,
      status,
      message,
    });

    it("announces a completed apply and refreshes the pending list, the library badges and the book", async () => {
      renderWithRouter();
      await screen.findByText(/The Way of Kings/);
      const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

      act(() => hub.emit(SignalREvents.SingleBookOperationComplete, completion("completed")));

      expect(notifications.success).toHaveBeenCalledWith("Metadata changes applied");
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["metadataRefresh"] });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["books"] });
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["bookDetail", 1] });
    });

    it("says there was nothing to apply when the pending changes were already gone", async () => {
      renderWithRouter();
      await screen.findByText(/The Way of Kings/);

      act(() => hub.emit(SignalREvents.SingleBookOperationComplete, completion("nothingToApply")));

      expect(notifications.info).toHaveBeenCalledWith(
        "Nothing to apply — this book had no pending changes left",
      );
      expect(notifications.success).not.toHaveBeenCalled();
    });

    it("shows the server's message when the apply fails", async () => {
      renderWithRouter();
      await screen.findByText(/The Way of Kings/);

      act(() =>
        hub.emit(
          SignalREvents.SingleBookOperationComplete,
          completion("failed", "Another operation is already modifying audiobook 1."),
        ),
      );

      expect(notifications.error).toHaveBeenCalledWith(
        "Another operation is already modifying audiobook 1.",
      );
    });

    it("tells the user how to resolve a target that already exists", async () => {
      renderWithRouter();
      await screen.findByText(/The Way of Kings/);

      act(() =>
        hub.emit(
          SignalREvents.SingleBookOperationComplete,
          completion("targetExists", "'/library/A/Book.m4b' already exists"),
        ),
      );

      expect(notifications.error).toHaveBeenCalledWith("'/library/A/Book.m4b' already exists", {
        description: "Open the row and apply again to be asked whether to replace it.",
      });
    });

    it("ignores the select operation's completion, which belongs to the pending-matches page", async () => {
      renderWithRouter();
      await screen.findByText(/The Way of Kings/);

      act(() =>
        hub.emit(SignalREvents.SingleBookOperationComplete, {
          ...completion("completed"),
          operation: SingleBookOperations.pendingMatchSelect,
        }),
      );

      expect(notifications.success).not.toHaveBeenCalled();
    });
  });
});
