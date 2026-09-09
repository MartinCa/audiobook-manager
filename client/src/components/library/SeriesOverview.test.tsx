import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalREvents } from "@/constants/signalrEvents";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";
import { seriesApi } from "@/services/api";
import type { HubEventHandler, SignalRContextValue } from "@/context/SignalRContext";

vi.mock("@/services/api", () => ({
  seriesApi: {
    getSeriesPage: vi.fn(),
    getSeriesCounts: vi.fn(),
    startRefreshAll: vi.fn(),
    getMatchCandidates: vi.fn(),
    startBulkMatch: vi.fn(),
  },
}));

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false } },
});

function makeSignalR() {
  const handlers = new Map<string, HubEventHandler<unknown>[]>();
  return {
    connection: null,
    isConnected: false,
    on: vi.fn((event: string, handler: HubEventHandler<unknown>) => {
      const list = handlers.get(event) ?? [];
      list.push(handler);
      handlers.set(event, list);
    }),
    off: vi.fn((event: string, handler: HubEventHandler<unknown>) => {
      handlers.set(
        event,
        (handlers.get(event) ?? []).filter((h) => h !== handler),
      );
    }),
    onReconnected: vi.fn(),
    offReconnected: vi.fn(),
    emit: (event: string, data: unknown) => {
      for (const handler of handlers.get(event) ?? []) handler(data);
    },
  };
}

let signalR: ReturnType<typeof makeSignalR>;

function renderWithProviders(initialEntry = "/library/series") {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [initialEntry] }),
  });

  return render(
    <ThemeProvider defaultTheme="system" storageKey="theme">
      <SignalRContext.Provider value={signalR as SignalRContextValue}>
        <QueryClientProvider client={queryClient}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      </SignalRContext.Provider>
    </ThemeProvider>,
  );
}

const counts = { total: 120, matched: 118, unmatched: 2 };

function makeSeries(id: number) {
  return {
    id,
    name: `Series ${String(id).padStart(2, "0")}`,
    authors: ["An Author"],
    ownedBookCount: 1,
    isMatched: id % 2 === 0,
    matchedSourceName: "Hardcover",
    matchedSourceId: String(id),
    matchedSourceUrl: null,
    matchConfidence: 0.9,
    lastRefreshedAt: null,
    expectedBookCount: 1,
    missingBookCount: 0,
    ignoredBookCount: 0,
    includeOmnibusEditions: false,
  };
}

const page0 = { items: Array.from({ length: 50 }, (_, i) => makeSeries(i + 1)), totalCount: 120 };
const page1 = { items: Array.from({ length: 50 }, (_, i) => makeSeries(i + 51)), totalCount: 120 };

describe("SeriesOverview", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    signalR = makeSignalR();
    vi.mocked(seriesApi.getSeriesCounts).mockResolvedValue(counts);
    vi.mocked(seriesApi.getSeriesPage).mockResolvedValue(page0);
  });

  it("renders series from the server page and sizes the pager and badges from server totals", async () => {
    renderWithProviders();

    expect(await screen.findByText("Series 01")).toBeInTheDocument();
    expect(screen.getByText(/Series \(120\)/)).toBeInTheDocument();
    expect(screen.getByText(/Showing 1–50 of 120/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Bulk Match (2)" })).toBeInTheDocument();
  });

  it("pages server-side: Next requests the following page and renders its items", async () => {
    const mountCall = vi.mocked(seriesApi.getSeriesPage);
    mountCall.mockResolvedValueOnce(page0).mockResolvedValueOnce(page1);

    renderWithProviders();

    const nextButton = await screen.findByRole("button", { name: "Next" });
    nextButton.click();

    await waitFor(() => {
      expect(seriesApi.getSeriesPage).toHaveBeenCalledWith(1, 50, "");
    });

    expect(await screen.findByText("Series 51")).toBeInTheDocument();
    expect(screen.getByText(/Showing 51–100 of 120/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Previous" })).toBeEnabled();
  });

  it("sends the debounced search filter to the server instead of filtering client-side", async () => {
    renderWithProviders();

    const searchInput = await screen.findByPlaceholderText("Filter series or authors...");
    fireEvent.change(searchInput, { target: { value: "mist" } });

    await waitFor(() => {
      // The debounced term lands in the TanStack Query key and the server is asked for the
      // filtered page, from page 0.
      expect(seriesApi.getSeriesPage).toHaveBeenCalledWith(0, 50, "mist");
    });
  });

  // Regression for the review finding: a refresh-all can match previously-unmatched series, i.e.
  // shrink the list, while the user sits on a later page. Completion used to leave the fetch
  // asking for a page that no longer exists - it came back empty and the section was stuck on a
  // dead-end. Completion must drop back to page 0.
  it("drops back to page 0 when the refresh-all completes", async () => {
    const getSeriesPage = vi.mocked(seriesApi.getSeriesPage);
    getSeriesPage.mockImplementation((page) =>
      Promise.resolve(
        page === 1 ? { ...page1, totalCount: page1Total } : { ...page0, totalCount: page0Total },
      ),
    );
    let page0Total = 120;
    let page1Total = 120;

    renderWithProviders();

    expect(await screen.findByText(/Showing 1–50 of 120/)).toBeInTheDocument();
    screen.getByRole("button", { name: "Next" }).click();
    await waitFor(() => {
      expect(seriesApi.getSeriesPage).toHaveBeenCalledWith(1, 50, "");
    });
    expect(screen.getByText(/Showing 51–100 of 120/)).toBeInTheDocument();

    // The refresh matched most unmatched series: the list shrinks to 50, so page 1 no longer
    // exists. The completion event arrives and the section must land back on page 0.
    page0Total = 50;
    page1Total = 50;
    signalR.emit(SignalREvents.SeriesRefreshComplete, {
      totalProcessed: 100,
      totalSucceeded: 70,
      totalFailed: 0,
    });

    // Only the page-0 fetch is issued after completion...
    await waitFor(() => {
      expect(seriesApi.getSeriesPage).toHaveBeenCalledWith(0, 50, "");
    });
    // ...and its items render instead of a dead-end empty page.
    expect(await screen.findByText("Series 01")).toBeInTheDocument();
    expect(screen.queryByText(/Showing 51–100 of 120/)).not.toBeInTheDocument();
  });
});
