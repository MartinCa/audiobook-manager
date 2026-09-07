import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";
import type * as SonnerModule from "sonner";
import { toast } from "sonner";

vi.mock("sonner", async (importOriginal) => {
  const actual = await importOriginal<typeof SonnerModule>();
  return {
    ...actual,
    toast: {
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
      warning: vi.fn(),
    },
  };
});

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

  const mockSignalRValue = {
    connection: null,
    isConnected: false,
    on: vi.fn(),
    off: vi.fn(),
    onReconnected: vi.fn(),
    offReconnected: vi.fn(),
  };

  const sampleItems: PendingMetadataRefreshListItem[] = [
    {
      audiobookId: 1,
      bookName: "The Way of Kings",
      authors: ["Brandon Sanderson"],
      fetchedAt: "2026-09-01T12:00:00Z",
      sourceName: "Goodreads",
    },
    {
      audiobookId: 2,
      bookName: "Words of Radiance",
      authors: ["Brandon Sanderson"],
      fetchedAt: "2026-09-01T12:05:00Z",
      sourceName: "Goodreads",
    },
  ];

  beforeEach(() => {
    vi.clearAllMocks();
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
        <SignalRContext.Provider value={mockSignalRValue}>
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
      expect(toast.success).toHaveBeenCalledWith("Metadata refresh started in background");
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
    // Local midnight on 2026-09-01, converted to UTC.
    expect(cutoffDateToUtcIso("2026-09-01")).toBe(arg);
    expect(new Date(cutoffDateToUtcIso("2026-09-01")!).toISOString().slice(0, 10)).toBe(
      "2026-09-01",
    );
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
      expect(metadataRefreshApi.getPendingPage).toHaveBeenCalledWith(1, 50);
    });
    const prev = screen.getByRole("button", { name: /previous/i });
    expect(prev).not.toBeDisabled();
    expect(screen.getByText(/Showing 51–51 of 51/)).toBeInTheDocument();
  });
});
