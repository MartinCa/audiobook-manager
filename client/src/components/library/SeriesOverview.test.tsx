import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";
import { seriesApi } from "@/services/api";

vi.mock("@/services/api", () => ({
  seriesApi: {
    getSeriesPage: vi.fn(),
    getSeriesCounts: vi.fn(),
    startRefreshAll: vi.fn(),
  },
}));

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false } },
});

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

function renderWithProviders(initialEntry = "/library/series") {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [initialEntry] }),
  });

  return render(
    <ThemeProvider defaultTheme="system" storageKey="theme">
      <SignalRContext.Provider value={mockSignalRValue}>
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
});
