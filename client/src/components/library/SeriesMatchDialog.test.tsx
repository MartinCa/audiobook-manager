import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SignalRContext } from "@/context/SignalRContext";
import { SeriesMatchDialog } from "./SeriesMatchDialog";
import { seriesApi } from "@/services/api";

vi.mock("@/services/api", () => ({
  seriesApi: {
    getSeriesPage: vi.fn(),
    getMatchCandidates: vi.fn(),
    startBulkMatch: vi.fn(),
  },
}));

vi.mock("sonner", () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

function makeUnmatched(id: number) {
  return {
    id,
    name: `Unmatched ${String(id).padStart(2, "0")}`,
    authors: ["An Author"],
    ownedBookCount: 1,
    isMatched: false,
    matchedSourceName: null,
    matchedSourceId: null,
    matchedSourceUrl: null,
    matchConfidence: null,
    lastRefreshedAt: null,
    expectedBookCount: 0,
    missingBookCount: 0,
    ignoredBookCount: 0,
    includeOmnibusEditions: false,
  };
}

const firstPage = {
  items: Array.from({ length: 30 }, (_, i) => makeUnmatched(i + 1)),
  totalCount: 30,
};

function renderDialog() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <SignalRContext.Provider value={mockSignalRValue}>
      <QueryClientProvider client={queryClient}>
        <SeriesMatchDialog open onOpenChange={() => {}} />
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
}

describe("SeriesMatchDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(seriesApi.getSeriesPage).mockResolvedValue(firstPage);
    vi.mocked(seriesApi.getMatchCandidates).mockResolvedValue([]);
  });

  it("pages the unmatched list itself instead of receiving the whole array", async () => {
    renderDialog();

    expect(await screen.findByText("Unmatched 01")).toBeInTheDocument();
    expect(screen.getByText(/30 of 30 unmatched selected/)).toBeInTheDocument();

    // The dialog requested only unmatched series from the server.
    expect(seriesApi.getSeriesPage).toHaveBeenCalledWith(0, 50, undefined, false);
  });

  it("matches everything in one click via the null subset rather than enumerating pages", async () => {
    renderDialog();

    await screen.findByText("Unmatched 01");
    const matchAll = await screen.findByRole("button", { name: "Match All Unmatched (30)" });
    matchAll.click();

    await waitFor(() => {
      // No second argument (seriesNames): the backend treats a missing list as "all unmatched".
      expect(seriesApi.startBulkMatch).toHaveBeenCalledWith(0.85);
    });
  });

  it("caps preview suggestions instead of issuing one request per unmatched row", async () => {
    renderDialog();

    await screen.findByText("Unmatched 01");
    const preview = await screen.findByRole("button", { name: /Preview Suggestions/ });
    preview.click();

    await waitFor(() => {
      // 30 unmatched rows are on the page, but the preview cap is 20 sequential lookups.
      expect(seriesApi.getMatchCandidates).toHaveBeenCalledTimes(20);
    });
    expect(
      screen.getByText(/Suggestions are previewed for the first 20 series/),
    ).toBeInTheDocument();
  });

  it("pages the selection set so names picked on page one stay selected on page two", async () => {
    vi.mocked(seriesApi.getSeriesPage)
      .mockResolvedValueOnce({
        items: Array.from({ length: 50 }, (_, i) => makeUnmatched(i + 1)),
        totalCount: 60,
      })
      .mockResolvedValueOnce({
        items: Array.from({ length: 10 }, (_, i) => makeUnmatched(i + 51)),
        totalCount: 60,
      });

    renderDialog();

    // Deselect page one's default "all selected" by toggling one box off, then go to page two.
    const checkboxes = await screen.findAllByRole("checkbox");
    checkboxes[0]!.click();

    expect(await screen.findByRole("button", { name: "Match Selected (49)" })).toBeInTheDocument();

    const nextButton = screen.getByRole("button", { name: "Next" });
    nextButton.click();

    await waitFor(() => {
      expect(seriesApi.getSeriesPage).toHaveBeenCalledWith(1, 50, undefined, false);
    });
    expect(await screen.findByText("Unmatched 51")).toBeInTheDocument();
  });
});
