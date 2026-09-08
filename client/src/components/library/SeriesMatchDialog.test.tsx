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

  // Regression for the review finding: "Select all on page" used to overwrite the whole
  // selection set with the current page's names, so picks made on other pages were silently
  // dropped the moment the user clicked it on a later page.
  it("unions page names with the existing selection instead of overwriting other pages", async () => {
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

    // Deselect "Unmatched 01" on page one, so the selection becomes an explicit 49-name set.
    const checkboxes = await screen.findAllByRole("checkbox");
    checkboxes[0]!.click();
    expect(await screen.findByRole("button", { name: "Match Selected (49)" })).toBeInTheDocument();

    // Page two: "Select all on page" must keep those 49 names and add the 10 on this page.
    screen.getByRole("button", { name: "Next" }).click();
    await screen.findByText("Unmatched 51");

    screen.getByRole("button", { name: "Select all on page" }).click();

    expect(await screen.findByRole("button", { name: "Match Selected (59)" })).toBeInTheDocument();

    screen.getByRole("button", { name: "Match Selected (59)" }).click();

    await waitFor(() => {
      const [, seriesNames] = vi.mocked(seriesApi.startBulkMatch).mock.calls.at(-1)!;
      expect(seriesNames).toHaveLength(59);
      expect(seriesNames).toContain("Unmatched 02");
      expect(seriesNames).toContain("Unmatched 51");
      expect(seriesNames).toContain("Unmatched 60");
      expect(seriesNames).not.toContain("Unmatched 01");
    });
  });

  // Regression for the review finding: while the selection is still implicit (null - "everything
  // on the loaded page selected"), paging used to leave the set null, so page 1's names were not
  // held anywhere. On page 2 the button read "Clear Selection" (page 2's length matched itself),
  // and either that or a "Select all on page" would have produced a bulk match without page 1.
  // Navigating must materialize the implicit set, so page 1's default selection survives and
  // "Select all on page" unions page 2's names into it.
  it("keeps page one's implicit default selection when selecting all on a later page", async () => {
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

    // Page 1 loads with the implicit "all selected" default - no explicit selection yet.
    expect(await screen.findByText("Unmatched 01")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Match Selected (50)" })).toBeInTheDocument();

    // Navigating materializes page 1's 50 names, so page 2's rows are not selected and the
    // button honestly reads "Select all on page" instead of comparing page 2 against itself.
    screen.getByRole("button", { name: "Next" }).click();

    expect(await screen.findByText("Unmatched 51")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Select all on page" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Match Selected (50)" })).toBeInTheDocument();

    screen.getByRole("button", { name: "Select all on page" }).click();

    // Both pages are now in the selection: page 2's 10 unioned onto page 1's 50.
    expect(await screen.findByRole("button", { name: "Match Selected (60)" })).toBeInTheDocument();

    screen.getByRole("button", { name: "Match Selected (60)" }).click();

    await waitFor(() => {
      const [, seriesNames] = vi.mocked(seriesApi.startBulkMatch).mock.calls.at(-1)!;
      expect(seriesNames).toHaveLength(60);
      expect(seriesNames).toContain("Unmatched 01");
      expect(seriesNames).toContain("Unmatched 25");
      expect(seriesNames).toContain("Unmatched 51");
      expect(seriesNames).toContain("Unmatched 60");
    });
  });
});
