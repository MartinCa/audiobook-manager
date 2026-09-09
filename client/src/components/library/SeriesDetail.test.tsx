import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";
import { seriesApi } from "@/services/api";
import type { SeriesDetail, SeriesExpectedBook, SeriesOwnedBook } from "@/types/Series";

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

function missingBook(id: number, title: string, position?: string): SeriesExpectedBook {
  return {
    id,
    title,
    position: position ?? null,
    year: 2000 + id,
    sourceUrl: null,
    isIgnored: false,
  };
}

const defaultOwned: SeriesOwnedBook = {
  id: 10,
  bookName: "The Final Empire",
  seriesPart: "1",
  year: 2006,
  authors: ["Brandon Sanderson"],
  narrators: ["Michael Kramer"],
  durationInSeconds: 88000,
};

function makeDetail(
  missingItems: SeriesExpectedBook[],
  missingTotal: number,
  ownedItems: SeriesOwnedBook[] = [defaultOwned],
  ignoredTotal = 0,
): SeriesDetail {
  return {
    overview: {
      id: 1,
      name: "Mistborn",
      authors: ["Brandon Sanderson"],
      ownedBookCount: ownedItems.length,
      isMatched: true,
      matchedSourceName: "Hardcover",
      matchedSourceId: "123",
      matchedSourceUrl: "https://hardcover.app/series/mistborn",
      matchConfidence: 0.98,
      lastRefreshedAt: "2026-01-01T00:00:00Z",
      expectedBookCount: missingTotal,
      missingBookCount: missingTotal,
      ignoredBookCount: ignoredTotal,
      includeOmnibusEditions: false,
    },
    ownedBooks: {
      items: ownedItems,
      totalCount: ownedItems.length,
    },
    missingBooks: {
      items: missingItems,
      totalCount: missingTotal,
    },
    ignoredBooks: {
      items: [],
      totalCount: ignoredTotal,
    },
  };
}

function renderWithProviders(initialEntry = "/library/series/Mistborn") {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [initialEntry] }),
  });

  return {
    router,
    ...render(
      <ThemeProvider defaultTheme="system" storageKey="theme">
        <SignalRContext.Provider value={mockSignalRValue}>
          <QueryClientProvider client={queryClient}>
            <RouterProvider router={router} />
          </QueryClientProvider>
        </SignalRContext.Provider>
      </ThemeProvider>,
    ),
  };
}

describe("SeriesDetail", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("renders series detail with matched provider and books", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(
      makeDetail([missingBook(20, "The Alloy of Law", "4")], 7),
    );

    renderWithProviders();

    const ownedBookLink = await screen.findByRole("link", { name: /The Final Empire/ });
    expect(ownedBookLink).toHaveAttribute("href", "/library/book/10");
    expect(ownedBookLink).not.toHaveAttribute("target");
    expect(screen.getByText("Matched to Hardcover")).toBeInTheDocument();
    expect(screen.getByText(/The Alloy of Law/)).toBeInTheDocument();
    expect(screen.getByText("Ignore")).toBeInTheDocument();
    expect(
      screen.getByText("Include omnibus/box-set editions in missing books list"),
    ).toBeInTheDocument();
    // Sections display their full totals, not just the loaded page.
    expect(screen.getByText(/Owned Books \(1\)/)).toBeInTheDocument();
    expect(screen.getByText(/Missing Books \(7\)/)).toBeInTheDocument();
  });

  // The three sections used to be three separate queries to the same endpoint - each computing
  // (and discarding) the other sections' default pages. One call carries all three page cursors.
  it("fetches the whole detail in a single call carrying every section's page", async () => {
    const getSeriesDetail = vi
      .spyOn(seriesApi, "getSeriesDetail")
      .mockResolvedValue(makeDetail([missingBook(20, "The Alloy of Law")], 7));

    renderWithProviders();

    await screen.findByText(/The Alloy of Law/);

    expect(getSeriesDetail).toHaveBeenCalledTimes(1);
    expect(getSeriesDetail).toHaveBeenCalledWith("Mistborn", {
      ownedPage: 0,
      ownedPageSize: 50,
      missingPage: 0,
      missingPageSize: 50,
      ignoredPage: 0,
      ignoredPageSize: 50,
    });
  });

  // Regression for the review finding: a total that shrinks while the user sits on the last
  // missing-books page (ignoring that page's only row) used to leave the section fetching page 1
  // of a list that no longer has one - it came back empty, and the section was stuck showing a
  // dead-end "no missing books" heading over the still-non-zero total, with the pager gone.
  it("returning to page 0 after shrinking the missing list instead of a dead-end empty page", async () => {
    const getSeriesDetail = vi.spyOn(seriesApi, "getSeriesDetail");
    vi.spyOn(seriesApi, "ignoreExpectedBook").mockResolvedValue(undefined);

    let missingTotal = 51;
    // 51 missing books: page 0 holds 50, page 1 holds the 51st - until the ignore shrinks the
    // total to 50, at which point only page 0 exists.
    getSeriesDetail.mockImplementation((_name, params) => {
      const firstFifty = Array.from({ length: 50 }, (_, i) =>
        missingBook(i + 1, `Book ${String(i + 1).padStart(2, "0")}`, String(i + 1)),
      );
      return Promise.resolve(
        params?.missingPage === 1
          ? makeDetail(missingTotal > 50 ? [missingBook(51, "Book 51", "51")] : [], missingTotal)
          : makeDetail(firstFifty, missingTotal),
      );
    });

    renderWithProviders();

    // Page to the last page of the missing section; the owned section has a single row so the
    // only "Next" button is the missing section's.
    await screen.findByText(/Book 01/);
    screen.getByRole("button", { name: "Next" }).click();

    await waitFor(() => {
      const last = getSeriesDetail.mock.calls.at(-1)!;
      expect(last[1]?.missingPage).toBe(1);
    });
    expect(await screen.findByText(/Book 51/)).toBeInTheDocument();

    // Ignore the only book on this page: the section becomes 50 items - no page 1 left.
    screen.getByRole("button", { name: "Ignore" }).click();
    missingTotal = 50;

    // The section must land back on page 0 and render its content, header and items agreeing.
    await waitFor(() => {
      const last = getSeriesDetail.mock.calls.at(-1)!;
      expect(last[1]?.missingPage).toBe(0);
    });
    expect(await screen.findByText(/Book 01/)).toBeInTheDocument();
    expect(screen.getByText(/Missing Books \(50\)/)).toBeInTheDocument();
  });

  it("selects owned books and reflects the page selection in the select-all checkbox", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(
      makeDetail([], 0, [
        { ...defaultOwned },
        {
          id: 11,
          bookName: "The Well of Ascension",
          seriesPart: "2",
          year: 2007,
          authors: ["Brandon Sanderson"],
          narrators: ["Michael Kramer"],
          durationInSeconds: 91000,
        },
      ]),
    );

    renderWithProviders();

    const ownedBookLink = await screen.findByRole("link", { name: /The Final Empire/ });
    expect(ownedBookLink).toHaveAttribute("href", "/library/book/10");

    const selectAll = screen.getByRole("checkbox", { name: "Select page" });
    expect(selectAll).toHaveAttribute("aria-checked", "false");

    // Picking just one of the two owned books makes the select-all indeterminate.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select The Final Empire" }));
    expect(selectAll).toHaveAttribute("aria-checked", "mixed");

    // Both picked: select-all reads fully checked, and the row title carries the series-part
    // prefix while the redundant "Series:" span stays hidden.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select The Well of Ascension" }));
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
    expect(screen.getByText("#1 The Final Empire")).toBeInTheDocument();
    expect(screen.queryByText(/Series: Mistborn/)).not.toBeInTheDocument();

    // Clearing the page through the select-all deselects every row.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select page" }));
    expect(screen.getByRole("checkbox", { name: "Select The Final Empire" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
    expect(screen.getByRole("checkbox", { name: "Select The Well of Ascension" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
  });

  // Regression for the review finding: the selection used to be reset only by comparing the
  // prev-series during render, and no test actually changed the route param, so nothing proved
  // the reset fired. Navigating to a second series whose roster reuses the same book ids is the
  // exact case that would leak a wrong selection: the same id is now a different book.
  it("clears the owned selection when navigating to a different series", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockImplementation((name) =>
      Promise.resolve(
        makeDetail(
          [],
          0,
          name === "Mistborn"
            ? [defaultOwned]
            : [{ ...defaultOwned, bookName: "Words of Radiance" }],
        ),
      ),
    );

    const { router } = renderWithProviders();
    await screen.findByRole("link", { name: /The Final Empire/ });

    fireEvent.click(screen.getByRole("checkbox", { name: "Select The Final Empire" }));
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "true",
    );

    await router.navigate({ href: "/library/series/Stormlight" });

    // The new series' roster renders with the same book id (10): only a real reset of the
    // selection - not the id changing out from under it - can leave it unchecked.
    await screen.findByRole("link", { name: /Words of Radiance/ });
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
    expect(screen.getByRole("checkbox", { name: "Select Words of Radiance" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
    expect(
      screen.queryByRole("checkbox", { name: "Select The Final Empire" }),
    ).not.toBeInTheDocument();
  });
});
