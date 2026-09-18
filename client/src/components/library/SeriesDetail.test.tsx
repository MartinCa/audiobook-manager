import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent, within } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";
import { SignalREvents } from "@/constants/signalrEvents";
import { notifications } from "@/lib/notifications";
import { seriesApi } from "@/services/api";
import type {
  SeriesDetail,
  SeriesExpectedBook,
  SeriesOwnedBook,
  SeriesPartMismatch,
} from "@/types/Series";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

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
  partMismatchItems: SeriesPartMismatch[] = [],
  partMismatchTotal = 0,
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
    partMismatches: {
      items: partMismatchItems,
      totalCount: partMismatchTotal,
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

function seriesDeleteCompleteHandler(): (data: {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
  errored: boolean;
}) => void {
  const call = [...mockSignalRValue.on.mock.calls]
    .reverse()
    .find(([name]) => name === SignalREvents.SeriesDeleteComplete);
  expect(call, "a SeriesDeleteComplete handler was registered").toBeDefined();
  return call![1] as (data: {
    totalProcessed: number;
    totalSucceeded: number;
    totalFailed: number;
    errored: boolean;
  }) => void;
}

/** Opens the delete confirmation dialog and clicks its destructive confirm button. */
async function confirmDeleteSeries() {
  fireEvent.click(screen.getByRole("button", { name: "Delete Series" }));
  const dialog = await screen.findByRole("dialog");
  fireEvent.click(within(dialog).getByRole("button", { name: "Delete Series" }));
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
    expect(screen.getAllByText(/The Alloy of Law/).length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText("Ignore")).toBeInTheDocument();
    expect(
      screen.getByText("Include omnibus/box-set editions in missing books list"),
    ).toBeInTheDocument();
    // Sections display their full totals, not just the loaded page.
    expect(screen.getByText(/Owned Books \(1\)/)).toBeInTheDocument();
    expect(screen.getByText(/Missing Books \(7\)/)).toBeInTheDocument();

    // Critical info first: the header shows the matched-to source indication twice - once in the
    // header where the source NAME is the link to the source page, and once in the Management
    // section's metadata block. There is no separate "View at source" link - the name is the link.
    const sourceLinks = screen.getAllByRole("link", { name: "Hardcover" });
    expect(sourceLinks.length).toBeGreaterThanOrEqual(2);
    expect(
      sourceLinks.every((l) => l.getAttribute("href") === "https://hardcover.app/series/mistborn"),
    ).toBe(true);
    expect(screen.getAllByText(/Matched to/).length).toBeGreaterThanOrEqual(2);
    expect(screen.queryByText(/View at source/)).not.toBeInTheDocument();

    // The management section owns the metadata provider details and the match/refresh actions.
    expect(screen.getByText("Management & Settings")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Re-match to Source" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh Online" })).toBeInTheDocument();
    expect(screen.getByText("Confidence: 98%")).toBeInTheDocument();
    expect(screen.getByText("Series Mapping Patterns (0)")).toBeInTheDocument();
  });

  // Regression for the review finding: the header already guarded "Matched to" with a non-empty
  // source name, but the Management card only checked isMatched and rendered "Matched to " against
  // an empty source name (still styled as a link when matchedSourceUrl happened to be set). Both
  // must share the guard - a matched series without a source name is the incomplete state, not a
  // dangling label.
  it("renders the incomplete-provider message for a matched series with no source name", async () => {
    const detail = makeDetail([], 0);
    detail.overview = {
      ...detail.overview,
      matchedSourceName: null,
      matchedSourceId: null,
      matchedSourceUrl: null,
    };
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(detail);

    renderWithProviders();

    await screen.findByRole("heading", { name: "Mistborn" });
    // Same guard in header and management card: never "Matched to " with an empty source name.
    expect(screen.queryByText(/Matched to/)).not.toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /Hardcover|View at source/ }),
    ).not.toBeInTheDocument();
    // Also no stray "View at source" (the source name is the only source link, and there is none).
    expect(screen.queryByText(/View at source/)).not.toBeInTheDocument();
    // The management card shows the incomplete-state guidance instead.
    expect(screen.getByText(/Not matched to an online metadata provider yet/)).toBeInTheDocument();
  });

  // Regression for the review finding: the header used to hide the missing count entirely when
  // it was zero, so a fully-owning matched series read as if the count were absent (and a series
  // that went from 1 missing to 0 got its number silently wiped). The critical header must show
  // owned and missing counts consistently for every matched series - 0 included.
  it("renders the missing count in the header for a matched series with nothing missing", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));

    renderWithProviders();

    const header = (await screen.findByRole("heading", { name: "Mistborn" })).closest("div")!;
    expect(within(header).getByText(/1 book owned/)).toBeInTheDocument();
    expect(within(header).getByText(/0 missing/)).toBeInTheDocument();
  });

  it("renders the missing count in the header for a matched series with missing books", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(
      makeDetail([missingBook(20, "The Alloy of Law")], 3),
    );

    renderWithProviders();

    const header = (await screen.findByRole("heading", { name: "Mistborn" })).closest("div")!;
    expect(within(header).getByText(/3 missing/)).toBeInTheDocument();
  });

  it("omits the missing count from the header for a series that is not matched", async () => {
    const detail = makeDetail([], 0);
    detail.overview = { ...detail.overview, isMatched: false };
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(detail);

    renderWithProviders();

    const header = (await screen.findByRole("heading", { name: "Mistborn" })).closest("div")!;
    expect(within(header).getByText(/1 book owned/)).toBeInTheDocument();
    expect(within(header).queryByText(/missing/)).not.toBeInTheDocument();
  });

  // Regression: the owned section used to serve a minimal DTO with no coverFilePath, so the
  // series view's rows always showed the placeholder. A book that carries one must render its
  // cover from the browse endpoint like the library and author views.
  it("renders the cover image for owned books that carry a coverFilePath", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(
      makeDetail([], 0, [{ ...defaultOwned, coverFilePath: "/covers/10.jpg" }]),
    );

    renderWithProviders();

    const img = await screen.findByAltText<HTMLImageElement>("The Final Empire");
    expect(img).toHaveAttribute("src", "/api/browse/audiobooks/10/cover");
    expect(img.closest("a")).toHaveAttribute("href", "/library/book/10");
  });

  // Regression for the same bug: a book with no cover must keep rendering the placeholder
  // rather than a broken image or nothing at all.
  it("renders a placeholder icon for owned books without a coverFilePath", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));

    const { container } = renderWithProviders();

    await screen.findByRole("link", { name: /The Final Empire/ });
    expect(screen.queryByAltText("The Final Empire")).not.toBeInTheDocument();
    expect(container.querySelector("svg.lucide-library")).not.toBeNull();
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
      partMismatchPage: 0,
      partMismatchPageSize: 50,
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

  function partMismatch(
    audiobookId: number,
    bookName: string,
    expectedPart: string,
    storedPart?: string | null,
  ): SeriesPartMismatch {
    return {
      audiobookId,
      bookName,
      storedPart: storedPart ?? null,
      expectedPart,
      rosterTitle: `${bookName} (source)`,
    };
  }

  it("renders the Part Mismatches section with stored and expected parts", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(
      makeDetail([], 0, [], 0, [partMismatch(10, "The Final Empire", "1", "7")], 1),
    );

    renderWithProviders();

    expect(await screen.findByText("Part Mismatches (1)")).toBeInTheDocument();
    expect(screen.getByText("The Final Empire")).toBeInTheDocument();
    expect(screen.getByText(/stored part/)).toBeInTheDocument();
    expect(screen.getByText(/shared with "The Final Empire \(source\)"/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Fix" })).toBeInTheDocument();
  });

  // Fixing a mismatch reuses expected-books/apply: the row's expected part and roster title are
  // exactly the natural key that endpoint resolves, so no second backend surface was introduced.
  // The refetch after the fix must render the section WITHOUT the fixed row - the whole point of
  // the fix - not a forever-identical mock.
  it("fixes a part mismatch through expected-books/apply and refetches the detail", async () => {
    let fixed = false;
    vi.spyOn(seriesApi, "applyMissingBook").mockImplementation(() => {
      fixed = true;
      return Promise.resolve();
    });
    const getSeriesDetail = vi.spyOn(seriesApi, "getSeriesDetail");
    getSeriesDetail.mockImplementation(() =>
      Promise.resolve(
        fixed
          ? makeDetail([], 0)
          : makeDetail([], 0, [], 0, [partMismatch(42, "Alloy of Law", "4", "9")], 1),
      ),
    );

    renderWithProviders();

    await screen.findByText("Part Mismatches (1)");
    fireEvent.click(screen.getByRole("button", { name: "Fix" }));

    await waitFor(() =>
      expect(seriesApi.applyMissingBook).toHaveBeenCalledWith(
        "Mistborn",
        42,
        "4",
        "Alloy of Law (source)",
      ),
    );
    await waitFor(() => {
      expect(getSeriesDetail.mock.calls.length).toBeGreaterThan(1);
    });
    await waitFor(() => {
      expect(screen.queryByText(/Part Mismatches/)).not.toBeInTheDocument();
    });
  });

  it("does not render the Part Mismatches section when there are none", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));

    renderWithProviders();

    await screen.findByRole("link", { name: /The Final Empire/ });
    expect(screen.queryByText(/Part Mismatches/)).not.toBeInTheDocument();
  });

  // Regression: TanStack Router decodes path params when it matches the URL, so `%25`/`%20` in
  // the route already arrive as `%`/space. The component must not decode the param again.
  it("renders a series name containing a literal percent from the single decoded route param", async () => {
    const getSeriesDetail = vi
      .spyOn(seriesApi, "getSeriesDetail")
      .mockResolvedValue(makeDetail([], 0));

    renderWithProviders("/library/series/10%25%20Happier");

    expect(await screen.findByRole("heading", { name: "10% Happier" })).toBeInTheDocument();
    expect(getSeriesDetail).toHaveBeenCalledWith("10% Happier", {
      ownedPage: 0,
      ownedPageSize: 50,
      missingPage: 0,
      missingPageSize: 50,
      ignoredPage: 0,
      ignoredPageSize: 50,
      partMismatchPage: 0,
      partMismatchPageSize: 50,
    });
  });

  it("opens the bulk missing-book match dialog from the Missing Books header", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(
      makeDetail([missingBook(20, "The Alloy of Law", "4")], 7),
    );
    const getBulk = vi
      .spyOn(seriesApi, "getBulkMissingBookCandidates")
      .mockResolvedValue({ items: [], totalCount: 0 });

    renderWithProviders();

    await screen.findByText(/Missing Books \(7\)/);

    // The bulk dialog is only offered when the series is matched and actually has missing books.
    expect(screen.getByRole("button", { name: "Match Missing Books" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Match Missing Books" }));

    await waitFor(() => {
      expect(getBulk).toHaveBeenCalledWith("Mistborn", 0, 50);
    });
    // The dialog opened and rendered its (here empty) review from the mocked page.
    expect(
      await screen.findByText("No missing books to match in this series."),
    ).toBeInTheDocument();
  });

  it("does not offer bulk missing-book matching when there is nothing missing", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));

    renderWithProviders();

    await screen.findByRole("heading", { name: "Mistborn" });
    expect(screen.queryByRole("button", { name: "Match Missing Books" })).not.toBeInTheDocument();
  });

  // --- Series mapping patterns (owned by this series, managed in the Management section) ---

  it("renders the series' mapping patterns in the management section", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));
    vi.spyOn(seriesApi, "getSeriesMappings").mockResolvedValue([
      { id: 1, regex: "^mistborn.*$", warnAboutPart: false },
      { id: 2, regex: "\\bhusk\\b", warnAboutPart: true },
    ]);

    renderWithProviders();

    expect(await screen.findByText("Series Mapping Patterns (2)")).toBeInTheDocument();
    expect(screen.getByText("^mistborn.*$")).toBeInTheDocument();
    expect(screen.getByText("\\bhusk\\b")).toBeInTheDocument();
    // Only the second pattern carries warn-on-part, so only one badge renders.
    expect(screen.getByText("warn on part")).toBeInTheDocument();
    expect(screen.queryAllByText("warn on part")).toHaveLength(1);
  });

  it("adds a mapping pattern through the management dialog with no target field", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));
    vi.spyOn(seriesApi, "getSeriesMappings").mockResolvedValue([]);
    const create = vi
      .spyOn(seriesApi, "createSeriesMapping")
      .mockResolvedValue({ id: 1, regex: "^wot.*$", warnAboutPart: false });

    renderWithProviders();

    await screen.findByText("Series Mapping Patterns (0)");
    fireEvent.click(screen.getByRole("button", { name: "Add Pattern" }));

    const dialog = await screen.findByRole("dialog");
    // The pattern has no target of its own: the dialog explains the target is this series and
    // offers only regex + warn-on-part.
    expect(dialog).toHaveTextContent('normalized to this series: "Mistborn"');
    expect(within(dialog).queryByLabelText(/Target Series Name/)).not.toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText(/Regex Pattern/), {
      target: { value: "^wot.*$" },
    });
    fireEvent.click(within(dialog).getByLabelText("Warn if series part is found"));
    fireEvent.click(within(dialog).getByRole("button", { name: "Add Pattern" }));

    await waitFor(() => {
      expect(create).toHaveBeenCalledWith("Mistborn", {
        regex: "^wot.*$",
        warnAboutPart: true,
      });
    });
  });

  it("edits a mapping pattern's regex and warn-on-part through the dialog", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));
    vi.spyOn(seriesApi, "getSeriesMappings").mockResolvedValue([
      { id: 5, regex: "^old.*$", warnAboutPart: false },
    ]);
    const update = vi
      .spyOn(seriesApi, "updateSeriesMapping")
      .mockResolvedValue({ id: 5, regex: "^new.*$", warnAboutPart: true });

    renderWithProviders();

    await screen.findByText("^old.*$");
    fireEvent.click(screen.getByRole("button", { name: "Edit pattern 5" }));

    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText(/Regex Pattern/), {
      target: { value: "^new.*$" },
    });
    fireEvent.click(within(dialog).getByLabelText("Warn if series part is found"));
    fireEvent.click(within(dialog).getByRole("button", { name: "Save Changes" }));

    await waitFor(() => {
      expect(update).toHaveBeenCalledWith("Mistborn", 5, {
        id: 5,
        regex: "^new.*$",
        warnAboutPart: true,
      });
    });
  });

  it("deletes a mapping pattern from the management list", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));
    vi.spyOn(seriesApi, "getSeriesMappings").mockResolvedValue([
      { id: 5, regex: "^mistborn.*$", warnAboutPart: false },
    ]);
    const remove = vi.spyOn(seriesApi, "deleteSeriesMapping").mockResolvedValue(undefined);

    renderWithProviders();

    await screen.findByText("^mistborn.*$");
    fireEvent.click(screen.getByRole("button", { name: "Delete pattern 5" }));

    await waitFor(() => {
      expect(remove).toHaveBeenCalledWith("Mistborn", 5);
    });
  });

  // --- Series deletion (fire-and-forget over SignalR) ---

  it("toasts a successful series deletion from the completion event", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));
    const startDelete = vi.spyOn(seriesApi, "startDeleteSeries").mockResolvedValue(undefined);

    renderWithProviders();
    await screen.findByRole("heading", { name: "Mistborn" });
    await confirmDeleteSeries();

    await waitFor(() => {
      expect(startDelete).toHaveBeenCalledWith("Mistborn");
    });

    // The delete runs in the background and reports completion over SignalR.
    seriesDeleteCompleteHandler()({
      totalProcessed: 1,
      totalSucceeded: 1,
      totalFailed: 0,
      errored: false,
    });

    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith("Series deleted");
    });
    // Closing the dialog and navigating back to the series list is the success path.
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("toasts the failure count when a series delete completes with un-cleared books", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));
    vi.spyOn(seriesApi, "startDeleteSeries").mockResolvedValue(undefined);

    renderWithProviders();
    await screen.findByRole("heading", { name: "Mistborn" });
    await confirmDeleteSeries();

    seriesDeleteCompleteHandler()({
      totalProcessed: 3,
      totalSucceeded: 1,
      totalFailed: 2,
      errored: false,
    });

    await waitFor(() => {
      expect(notifications.error).toHaveBeenCalledWith(
        "Series deleted with 2 books that could not be cleared",
      );
    });
    expect(notifications.success).not.toHaveBeenCalled();
  });

  it("keeps the dialog open and toasts the failure when the series delete background operation errored", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));
    vi.spyOn(seriesApi, "startDeleteSeries").mockResolvedValue(undefined);

    renderWithProviders();
    await screen.findByRole("heading", { name: "Mistborn" });
    await confirmDeleteSeries();

    // errored carries zero counts, otherwise indistinguishable from an empty successful delete.
    seriesDeleteCompleteHandler()({
      totalProcessed: 0,
      totalSucceeded: 0,
      totalFailed: 0,
      errored: true,
    });

    await waitFor(() => {
      expect(notifications.error).toHaveBeenCalledWith("Series deletion failed");
    });
    // The dialog stays open on the (possibly partially-cleared) series; no navigate-away.
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("toasts the error when starting the series delete is refused", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));
    vi.spyOn(seriesApi, "startDeleteSeries").mockRejectedValue(
      new Error("An operation is already in progress."),
    );

    renderWithProviders();
    await screen.findByRole("heading", { name: "Mistborn" });
    await confirmDeleteSeries();

    await waitFor(() => {
      expect(notifications.error).toHaveBeenCalledWith("An operation is already in progress.");
    });
    // The failed start releases the deleting state, so the dialog is not stuck on a spinner.
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByRole("button", { name: "Delete Series" })).toBeEnabled();
  });

  // --- Back navigation is a real link with a stable href, not a history-dependent button ---

  it("renders the visible back control as a real link to the series list", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));

    renderWithProviders();
    await screen.findByRole("heading", { name: "Mistborn" });

    const backLink = await screen.findByRole("button", { name: /back to series/i });
    expect(backLink.tagName).toBe("A");
    expect(backLink).toHaveAttribute("href", "/library/series");
  });

  it("renders a stable link back to the author route when the series was opened from an author", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));

    renderWithProviders("/library/series/Mistborn?authorId=5");
    await screen.findByRole("heading", { name: "Mistborn" });

    const backLink = await screen.findByRole("button", { name: /back to author/i });
    expect(backLink.tagName).toBe("A");
    expect(backLink).toHaveAttribute("href", "/library/authors/5");
  });

  // --- Applying a source-series-name adoption must move the detail page to the adopted name ---

  function seriesRefreshApplyCompleteHandler(): (data: {
    totalProcessed: number;
    totalSucceeded: number;
    totalFailed: number;
    effectiveSeriesName?: string | null;
    seriesName: string;
  }) => void {
    const call = [...mockSignalRValue.on.mock.calls]
      .reverse()
      .find(([name]) => name === SignalREvents.SeriesRefreshApplyComplete);
    expect(call, "a SeriesRefreshApplyComplete handler was registered").toBeDefined();
    return call![1] as (data: {
      totalProcessed: number;
      totalSucceeded: number;
      totalFailed: number;
      effectiveSeriesName?: string | null;
      seriesName: string;
    }) => void;
  }

  function mockRenamePendingSetup() {
    vi.spyOn(seriesApi, "getSeriesMappings").mockResolvedValue([]);
    vi.spyOn(seriesApi, "getSeriesPending").mockResolvedValue({
      seriesName: "Mistborn",
      sourceName: "Hardcover",
      sourceUrl: "https://hardcover.app/series/42",
      sourceSeriesName: "Mistborn Saga",
      fetchedAt: "2026-09-01T12:00:00Z",
      changes: [],
    });
    vi.spyOn(seriesApi, "applySeriesPending").mockResolvedValue(undefined);
  }

  it("navigates with replace to the adopted series route after a rename apply completes", async () => {
    mockRenamePendingSetup();
    const getSeriesDetail = vi
      .spyOn(seriesApi, "getSeriesDetail")
      .mockImplementation((name) =>
        Promise.resolve(
          name === "Mistborn" ? makeDetail([], 0) : makeDetail([], 0, [defaultOwned]),
        ),
      );

    const { router } = renderWithProviders();
    await screen.findByRole("heading", { name: "Mistborn" });

    await screen.findByRole("button", { name: "Review Changes" });
    fireEvent.click(screen.getByRole("button", { name: "Review Changes" }));
    await screen.findByText(/Review Series Refresh/);
    const adopt = await screen.findByRole("checkbox", { name: /Adopt source series name/ });
    fireEvent.click(adopt);
    await screen.findByRole("button", { name: "Apply rename" });
    fireEvent.click(screen.getByRole("button", { name: "Apply rename" }));

    await waitFor(() => {
      expect(seriesApi.applySeriesPending).toHaveBeenCalledWith("Mistborn", {
        adoptSourceSeriesName: true,
        selections: [],
      });
    });

    seriesRefreshApplyCompleteHandler()({
      totalProcessed: 1,
      totalSucceeded: 1,
      totalFailed: 0,
      seriesName: "Mistborn",
      effectiveSeriesName: "Mistborn Saga",
    });

    // The route moves to the adopted name (the old one 404s), and the detail refetches for it.
    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/library/series/Mistborn Saga");
    });
    await waitFor(() => {
      expect(getSeriesDetail.mock.calls.at(-1)![0]).toBe("Mistborn Saga");
    });
  });

  it("stays on the current route when the apply completes without a rename", async () => {
    mockRenamePendingSetup();
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));

    const { router } = renderWithProviders();
    await screen.findByRole("heading", { name: "Mistborn" });

    await screen.findByRole("button", { name: "Review Changes" });
    fireEvent.click(screen.getByRole("button", { name: "Review Changes" }));
    await screen.findByText(/Review Series Refresh/);
    // Arm a rename-only apply so the completion path runs, then complete it without an
    // effective name (e.g. the adoption failed): the series is still addressable under the old
    // name, so the page must not move.
    const adopt = await screen.findByRole("checkbox", { name: /Adopt source series name/ });
    fireEvent.click(adopt);
    const applyButton = await screen.findByRole("button", { name: "Apply rename" });
    fireEvent.click(applyButton);

    await waitFor(() => {
      expect(seriesApi.applySeriesPending).toHaveBeenCalledWith("Mistborn", {
        adoptSourceSeriesName: true,
        selections: [],
      });
    });

    // No adoption succeeded, so the completion carries no effective name and the route stays.
    seriesRefreshApplyCompleteHandler()({
      totalProcessed: 1,
      totalSucceeded: 0,
      totalFailed: 1,
      seriesName: "Mistborn",
    });

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/library/series/Mistborn");
    });
  });

  it("renders the name-alignment banner, not a misleading 0-change count, for a snapshot with no book changes", async () => {
    mockRenamePendingSetup();
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue(makeDetail([], 0));

    renderWithProviders();
    await screen.findByRole("heading", { name: "Mistborn" });

    // The snapshot exists only because the source series name differs ("Mistborn Saga"), so the
    // banner must say so instead of "0 pending changes from the last refresh".
    await screen.findByText(/Series name alignment pending/);
    screen.getByText(/Review it before it is written to your books/);
    expect(screen.queryByText(/0 pending changes/)).toBeNull();
    expect(screen.getByRole("button", { name: "Review Changes" })).toBeDefined();
  });
});
