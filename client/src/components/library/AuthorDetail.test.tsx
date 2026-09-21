import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";
import { browseApi, upcomingReleasesApi } from "@/services/api";
import type { AuthorDetail } from "@/types/AuthorDetail";

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

function makeDetail(
  seriesCount: number,
  standaloneCount: number,
  opts: {
    seriesItems?: number;
    standaloneItems?: number;
    authorId?: number;
    lastRefreshedAt?: string | null;
    missingBooks?: AuthorDetail["missingBooks"];
    upcomingBooks?: AuthorDetail["upcomingBooks"];
    ignoredBooks?: AuthorDetail["ignoredBooks"];
    missingSeries?: AuthorDetail["missingSeries"];
  } = {},
): AuthorDetail {
  const authorId = opts.authorId ?? 7;
  return {
    author: { id: authorId, name: "Brandon Sanderson", bookCount: seriesCount + standaloneCount },
    series: {
      count: opts.seriesItems ?? Math.min(seriesCount, 50),
      total: seriesCount,
      items: Array.from({ length: opts.seriesItems ?? Math.min(seriesCount, 50) }, (_, i) => ({
        id: i + 1,
        name: `Series ${String(i + 1).padStart(2, "0")}`,
        authors: ["Brandon Sanderson"],
        ownedBookCount: i + 1,
        isMatched: i % 2 === 0,
        matchedSourceName: i % 2 === 0 ? "Hardcover" : null,
        matchedSourceId: i % 2 === 0 ? String(i + 1) : null,
        matchedSourceUrl: null,
        matchConfidence: i % 2 === 0 ? 0.9 : null,
        lastRefreshedAt: null,
        expectedBookCount: i + 1,
        missingBookCount: i % 2 === 0 ? 1 : 0,
        ignoredBookCount: 0,
        includeOmnibusEditions: false,
        upcomingBookCount: 0,
        isFollowed: false,
      })),
    },
    standaloneBooks: {
      count: opts.standaloneItems ?? Math.min(standaloneCount, 50),
      total: standaloneCount,
      items: Array.from(
        { length: opts.standaloneItems ?? Math.min(standaloneCount, 50) },
        (_, i) => ({
          id: 100 + i,
          bookName: `Standalone ${String(i + 1).padStart(2, "0")}`,
          authors: ["Brandon Sanderson"],
          narrators: ["Michael Kramer"],
          genres: ["Fantasy"],
          year: 2000 + i,
          durationInSeconds: 8_000 + i,
        }),
      ),
    },
    lastRefreshedAt: opts.lastRefreshedAt ?? null,
    missingBooks: opts.missingBooks ?? [],
    upcomingBooks: opts.upcomingBooks ?? [],
    ignoredBooks: opts.ignoredBooks ?? [],
    // Defaults to null (section not computed): the opt-in flag is always sent now, so a test
    // that wants the Missing Series section passes a real page here.
    missingSeries: opts.missingSeries ?? null,
  };
}

function renderWithProviders(initialEntry = "/library/authors/7") {
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

describe("AuthorDetail", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  // The two sections used to be two separate queries to the same endpoint - each computing
  // (and discarding) the other section's default page. One call carries both section cursors.
  it("fetches the whole author detail in a single call carrying both sections' cursors", async () => {
    const getAuthorDetail = vi
      .spyOn(browseApi, "getAuthorDetail")
      .mockResolvedValue(makeDetail(90, 65));

    renderWithProviders();

    expect(await screen.findByText("Brandon Sanderson")).toBeInTheDocument();
    expect(screen.getByText(/Series \(90\)/)).toBeInTheDocument();
    expect(screen.getByText(/Standalone Audiobooks \(65\)/)).toBeInTheDocument();
    expect(screen.getByText("Series 01")).toBeInTheDocument();
    expect(screen.getByText(/Standalone 01/)).toBeInTheDocument();

    expect(getAuthorDetail).toHaveBeenCalledTimes(1);
    expect(getAuthorDetail).toHaveBeenCalledWith(7, {
      seriesLimit: 50,
      seriesOffset: 0,
      standaloneLimit: 50,
      standaloneOffset: 0,
      includeMissingSeries: true,
      missingSeriesLimit: 50,
      missingSeriesOffset: 0,
      standaloneSearch: "",
      standaloneFilters: {},
    });
  });

  // Regression: the visible back control used to be a history-back Button, so it had no href -
  // it could not be opened in a new tab or copied, unlike the not-found fallback's LinkButton.
  it("renders Back to Authors as a real link with a stable href", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(makeDetail(1, 1));

    renderWithProviders();

    const backBtn = await screen.findByRole("button", { name: /back to authors/i });
    expect(backBtn.tagName).toBe("A");
    expect(backBtn).toHaveAttribute("href", "/library/authors");
  });

  // Scope addition: the series section's pager used to hand-roll its own "Showing X-Y of Z" +
  // Previous/Next markup; it now shares SectionPager (same component the standalone-books
  // section and every other paged list use).
  it("renders the series section's pager through the shared SectionPager", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(makeDetail(90, 0));

    renderWithProviders();

    await screen.findByText("Series 01");
    expect(screen.getByText("Showing 1–50 of 90")).toBeInTheDocument();
  });

  it("pages one section through the same combined call, keeping the other section's page", async () => {
    const getAuthorDetail = vi.spyOn(browseApi, "getAuthorDetail");
    getAuthorDetail.mockResolvedValueOnce(makeDetail(90, 65)).mockResolvedValueOnce(
      makeDetail(90, 65, {
        seriesItems: 0,
      }),
    );

    renderWithProviders();

    await screen.findByText("Brandon Sanderson");

    // Both sections are multi-page, so paging the series section means clicking its pager's
    // Next (the series section renders before the standalone section).
    const nextButtons = screen.getAllByRole("button", { name: "Next" });
    expect(nextButtons).toHaveLength(2);
    nextButtons[0]!.click();

    await waitFor(() => {
      expect(getAuthorDetail).toHaveBeenCalledTimes(2);
      expect(getAuthorDetail).toHaveBeenLastCalledWith(7, {
        seriesLimit: 50,
        seriesOffset: 50,
        standaloneLimit: 50,
        standaloneOffset: 0,
        includeMissingSeries: true,
        missingSeriesLimit: 50,
        missingSeriesOffset: 0,
        standaloneSearch: "",
        standaloneFilters: {},
      });
    });
  });

  it("selects standalone books and reflects the page selection in the select-all checkbox", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(makeDetail(0, 2));

    renderWithProviders();

    await screen.findByText(/Standalone 01/);

    const selectAll = screen.getByRole("checkbox", { name: "Select page" });
    expect(selectAll).toHaveAttribute("aria-checked", "false");

    // Picking one of the two standalone books makes the select-all indeterminate.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 01" }));
    expect(selectAll).toHaveAttribute("aria-checked", "mixed");

    // Both picked: fully checked.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 02" }));
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "true",
    );

    // Deselect one: back to indeterminate.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 02" }));
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "mixed",
    );

    // Deselect the last one: the page is cleared entirely.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 01" }));
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
  });

  // Bug 8 unification: the standalone-books section gets the same search box the library list
  // has, wired through to the backend's standaloneSearch query param.
  it("debounces the standalone-books search box into a getAuthorDetail call", async () => {
    const getAuthorDetail = vi
      .spyOn(browseApi, "getAuthorDetail")
      .mockResolvedValue(makeDetail(0, 2));

    renderWithProviders();
    await screen.findByText(/Standalone 01/);

    const input = screen.getByPlaceholderText(/Search title, author/i);
    fireEvent.change(input, { target: { value: "standalone" } });

    await waitFor(
      () => {
        expect(getAuthorDetail).toHaveBeenLastCalledWith(
          7,
          expect.objectContaining({ standaloneSearch: "standalone" }),
        );
      },
      { timeout: 1000 },
    );
  });

  // Bug 8 unification: the standalone-books section gets the same option filters the library
  // list has.
  it("passes standalone-book option filters through and resets the standalone page", async () => {
    const getAuthorDetail = vi
      .spyOn(browseApi, "getAuthorDetail")
      .mockResolvedValue(makeDetail(0, 2));

    renderWithProviders();
    await screen.findByText(/Standalone 01/);

    fireEvent.click(screen.getByRole("button", { name: /Filters/ }));
    fireEvent.change(screen.getByLabelText("Duration (minutes) minimum"), {
      target: { value: "10" },
    });

    await waitFor(() => {
      expect(getAuthorDetail).toHaveBeenLastCalledWith(
        7,
        expect.objectContaining({
          standaloneOffset: 0,
          standaloneFilters: { minDurationInSeconds: 600 },
        }),
      );
    });
  });

  // Regression for the review finding: the selection used to be reset only by comparing the
  // prev-id during render, and no test actually changed the route param, so nothing proved the
  // reset fired. Navigating to a second author whose catalogue reuses the same book ids is the
  // exact case that would leak a wrong selection: the same id is now a different book.
  it("clears the selection when navigating to a different author", async () => {
    const getAuthorDetail = vi
      .spyOn(browseApi, "getAuthorDetail")
      .mockImplementation((id) => Promise.resolve(makeDetail(0, 2, { authorId: id })));
    const { router } = renderWithProviders("/library/authors/7");
    await screen.findByText(/Standalone 01/);

    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 01" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 02" }));
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "true",
    );

    await router.navigate({ href: "/library/authors/8" });

    await waitFor(() => {
      expect(getAuthorDetail).toHaveBeenLastCalledWith(8, {
        seriesLimit: 50,
        seriesOffset: 0,
        standaloneLimit: 50,
        standaloneOffset: 0,
        includeMissingSeries: true,
        missingSeriesLimit: 50,
        missingSeriesOffset: 0,
        standaloneSearch: "",
        standaloneFilters: {},
      });
    });

    // Author 8's standalone books reuse ids 100/101: only a real reset of the selection - not
    // the ids changing out from under it - can leave the new author's rows unchecked.
    await waitFor(() => {
      expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
        "aria-checked",
        "false",
      );
    });
    expect(screen.getByRole("checkbox", { name: "Select Standalone 01" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
    expect(screen.getByRole("checkbox", { name: "Select Standalone 02" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
  });

  // --- Missing/Upcoming standalone books: collapsed by default, mirroring SeriesDetail's ---

  it("renders Missing and Upcoming Books collapsed, with counts in the header", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(
      makeDetail(0, 0, {
        missingBooks: [
          { id: 1, title: "Warbreaker 2", isIgnored: false, year: 2019, sourceUrl: null },
        ],
        upcomingBooks: [
          {
            id: 2,
            title: "Stormlight 6",
            isIgnored: false,
            year: null,
            sourceUrl: null,
            releaseDate: "2027-03-01",
          },
        ],
      }),
    );
    vi.spyOn(browseApi, "getAuthorFollowStatus").mockResolvedValue({ isFollowed: false });
    vi.spyOn(browseApi, "getAuthorHardcoverMatch").mockResolvedValue({});

    renderWithProviders();

    expect(await screen.findByText("Missing Books (1)")).toBeInTheDocument();
    expect(screen.getByText("Upcoming Books (1)")).toBeInTheDocument();
    expect(screen.queryByText("Warbreaker 2")).not.toBeInTheDocument();
    expect(screen.queryByText("Stormlight 6")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Missing Books (1)" }));
    expect(screen.getByText("Warbreaker 2")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Upcoming Books (1)" }));
    expect(screen.getByText("Stormlight 6")).toBeInTheDocument();
    expect(screen.getByText(/releases 2027-03-01/)).toBeInTheDocument();
  });

  it("ignores a missing standalone book through the author expected-books/ignore endpoint", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(
      makeDetail(0, 0, {
        missingBooks: [
          { id: 1, title: "Warbreaker 2", isIgnored: false, year: 2019, sourceUrl: null },
        ],
      }),
    );
    vi.spyOn(browseApi, "getAuthorFollowStatus").mockResolvedValue({ isFollowed: false });
    vi.spyOn(browseApi, "getAuthorHardcoverMatch").mockResolvedValue({});
    const ignore = vi.spyOn(browseApi, "ignoreAuthorExpectedBook").mockResolvedValue(undefined);

    renderWithProviders();

    await screen.findByText("Missing Books (1)");
    fireEvent.click(screen.getByRole("button", { name: "Missing Books (1)" }));
    fireEvent.click(screen.getByRole("button", { name: "Ignore" }));

    await waitFor(() => {
      expect(ignore).toHaveBeenCalledWith(7, { id: 1 });
    });
  });

  it("unignores an ignored standalone book through the author expected-books/unignore endpoint", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(
      makeDetail(0, 0, {
        ignoredBooks: [
          { id: 1, title: "Warbreaker 2", isIgnored: true, year: 2019, sourceUrl: null },
        ],
      }),
    );
    vi.spyOn(browseApi, "getAuthorFollowStatus").mockResolvedValue({ isFollowed: false });
    vi.spyOn(browseApi, "getAuthorHardcoverMatch").mockResolvedValue({});
    const unignore = vi.spyOn(browseApi, "unignoreAuthorExpectedBook").mockResolvedValue(undefined);

    renderWithProviders();

    // The dismissed book no longer has its own Ignored section - it renders faded inside the
    // Missing list once the "show ignored" toggle (which carries the count) is switched on.
    expect(await screen.findByText(/show ignored books \(1\)/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Unignore" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("checkbox", { name: /show ignored books/i }));
    fireEvent.click(screen.getByRole("button", { name: "Missing Books (0)" }));
    expect(screen.getByRole("button", { name: "Unignore" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Unignore" }));

    await waitFor(() => {
      expect(unignore).toHaveBeenCalledWith(7, { id: 1 });
    });
  });

  // --- Show-ignored: dismissed entries render faded inside the section they classify to ---

  it("shows ignored books faded inside the section they classify to when the toggle is on", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(
      makeDetail(0, 0, {
        missingBooks: [
          { id: 1, title: "Missing Active", isIgnored: false, year: 2019, sourceUrl: null },
        ],
        upcomingBooks: [
          {
            id: 2,
            title: "Upcoming Active",
            isIgnored: false,
            year: null,
            sourceUrl: null,
            releaseDate: "2030-01-01",
          },
        ],
        ignoredBooks: [
          { id: 3, title: "Ignored Past Book", isIgnored: true, year: 2018, sourceUrl: null },
          {
            id: 4,
            title: "Ignored Future Book",
            isIgnored: true,
            year: null,
            sourceUrl: null,
            releaseDate: "2031-05-05",
          },
        ],
      }),
    );
    vi.spyOn(browseApi, "getAuthorFollowStatus").mockResolvedValue({ isFollowed: false });
    vi.spyOn(browseApi, "getAuthorHardcoverMatch").mockResolvedValue({});

    renderWithProviders();

    // Both sections name their counts from the active rows only; the toggle carries the ignored
    // count, and the dismissed rows stay hidden until it is switched on.
    expect(await screen.findByText(/Missing Books \(1\)/)).toBeInTheDocument();
    expect(screen.getByText(/Upcoming Books \(1\)/)).toBeInTheDocument();
    expect(screen.getByText(/show ignored books \(2\)/i)).toBeInTheDocument();
    expect(screen.queryByText("Ignored Past Book")).not.toBeInTheDocument();
    expect(screen.queryByText("Ignored Future Book")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("checkbox", { name: /show ignored books/i }));
    fireEvent.click(screen.getByRole("button", { name: "Missing Books (1)" }));
    fireEvent.click(screen.getByRole("button", { name: "Upcoming Books (1)" }));

    // The past-dated ignored row belongs to Missing, the future-dated one to Upcoming, and both
    // render in the low-emphasis (muted title) style - unlike the active rows beside them.
    const pastRow = screen.getByText("Ignored Past Book");
    expect(pastRow).toHaveClass("text-muted-foreground");
    const futureRow = screen.getByText("Ignored Future Book");
    expect(futureRow).toHaveClass("text-muted-foreground");
    expect(screen.getByText("Missing Active")).not.toHaveClass("text-muted-foreground");
    expect(screen.getByText("Upcoming Active")).not.toHaveClass("text-muted-foreground");
  });

  // --- Upcoming Releases: the whole section (heading included) hides when empty ---

  it("hides the Upcoming Releases section entirely once loaded with no releases", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(makeDetail(0, 0));
    vi.spyOn(browseApi, "getAuthorFollowStatus").mockResolvedValue({ isFollowed: false });
    vi.spyOn(browseApi, "getAuthorHardcoverMatch").mockResolvedValue({});
    vi.spyOn(upcomingReleasesApi, "getUpcomingReleases").mockResolvedValue({
      count: 0,
      total: 0,
      items: [],
    });

    renderWithProviders();

    await screen.findByText("Brandon Sanderson");
    await waitFor(() => {
      expect(upcomingReleasesApi.getUpcomingReleases).toHaveBeenCalled();
    });
    await waitFor(() => {
      expect(screen.queryByRole("heading", { name: /Upcoming Releases/ })).not.toBeInTheDocument();
    });
  });

  // Regression for the matched-but-unfollowed backend fix: once the roster returns entries for a
  // matched-but-unfollowed author, the section must render with its heading, not stay hidden.
  it("shows the Upcoming Releases section when the roster has entries", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(makeDetail(0, 0));
    vi.spyOn(browseApi, "getAuthorFollowStatus").mockResolvedValue({ isFollowed: false });
    vi.spyOn(browseApi, "getAuthorHardcoverMatch").mockResolvedValue({});
    vi.spyOn(upcomingReleasesApi, "getUpcomingReleases").mockResolvedValue({
      count: 1,
      total: 1,
      items: [
        {
          source: "Roster",
          id: null,
          expectedBookId: 42,
          title: "Stormlight 6",
          releaseDate: "2031-01-01",
          year: 2031,
          sourceName: "Hardcover",
          sourceUrl: null,
          sourceBookId: "999",
          imageUrl: null,
          authorId: 7,
          authorName: "Brandon Sanderson",
          seriesId: null,
          seriesName: null,
          seriesPosition: null,
        },
      ],
    });

    renderWithProviders();

    expect(await screen.findByRole("heading", { name: /Upcoming Releases/ })).toBeInTheDocument();
    expect(screen.getByText("Stormlight 6")).toBeInTheDocument();
  });

  // --- Missing Series: the paged opt-in section ---

  it("renders Missing Series with counts, a link to a matched series, and a disabled hint when unmatched", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(
      makeDetail(0, 0, {
        missingSeries: {
          count: 2,
          total: 2,
          items: [
            {
              sourceName: "Hardcover",
              sourceSeriesId: "101",
              sourceSeriesName: "Stormlight Archive",
              expectedCount: 3,
              missingCount: 2,
              upcomingCount: 1,
              ownedBookCount: 1,
              matchedSeriesId: 1,
              matchedSeriesName: "The Stormlight Archive",
            },
            {
              sourceName: "Hardcover",
              sourceSeriesId: "102",
              sourceSeriesName: "Unmatched Saga",
              expectedCount: 3,
              missingCount: 3,
              upcomingCount: 0,
              ownedBookCount: 0,
              matchedSeriesId: null,
              matchedSeriesName: null,
            },
          ],
        },
      }),
    );
    vi.spyOn(browseApi, "getAuthorFollowStatus").mockResolvedValue({ isFollowed: false });
    vi.spyOn(browseApi, "getAuthorHardcoverMatch").mockResolvedValue({});

    renderWithProviders();

    expect(await screen.findByText(/Missing Series \(2\)/)).toBeInTheDocument();

    // Collapsed by default, mirroring Missing/Upcoming Books - only the label and count show
    // until the user expands it.
    expect(screen.queryByText("The Stormlight Archive")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Missing Series (2)" }));

    // Matched series: the matched local name is shown, the badges carry the counts, and the row
    // links to the series detail with this author's id so "Back to Author" still works.
    expect(screen.getByText("The Stormlight Archive")).toBeInTheDocument();
    expect(screen.getByText("2 missing")).toBeInTheDocument();
    expect(screen.getByText("1 upcoming")).toBeInTheDocument();
    expect(screen.getByText("1 owned")).toBeInTheDocument();
    const seriesLink = screen.getByRole("link", { name: /View series/ });
    expect(seriesLink).toHaveAttribute(
      "href",
      "/library/series/The%20Stormlight%20Archive?authorId=7",
    );

    // Unmatched series: the source's series name is shown and the row renders the disabled
    // "Match series" hint in place of an actionable link (matching belongs on the series page).
    expect(screen.getByText("Unmatched Saga")).toBeInTheDocument();
    expect(screen.queryAllByRole("link", { name: /View series/ })).toHaveLength(1);
    expect(screen.getByText("Match series")).toBeInTheDocument();
  });

  it("pages the Missing Series section through the same combined detail call", async () => {
    const getAuthorDetail = vi.spyOn(browseApi, "getAuthorDetail");
    getAuthorDetail.mockImplementation((_id, params) => {
      const firstPage = params?.missingSeriesOffset === 0;
      return Promise.resolve(
        makeDetail(0, 0, {
          missingSeries: {
            count: firstPage ? 50 : 1,
            total: 51,
            items: Array.from({ length: firstPage ? 50 : 1 }, (_, i) => ({
              sourceName: "Hardcover",
              sourceSeriesId: String(firstPage ? i + 1 : 51),
              sourceSeriesName: `Source Series ${firstPage ? i + 1 : 51}`,
              expectedCount: 2,
              missingCount: 1,
              upcomingCount: 1,
              ownedBookCount: 0,
              matchedSeriesId: firstPage ? i + 1 : 51,
              matchedSeriesName: `Matched Series ${firstPage ? i + 1 : 51}`,
            })),
          },
        }),
      );
    });
    vi.spyOn(browseApi, "getAuthorFollowStatus").mockResolvedValue({ isFollowed: false });
    vi.spyOn(browseApi, "getAuthorHardcoverMatch").mockResolvedValue({});

    renderWithProviders();

    await screen.findByText(/Missing Series \(51\)/);
    fireEvent.click(screen.getByRole("button", { name: "Missing Series (51)" }));
    expect(screen.getByText("Matched Series 1")).toBeInTheDocument();

    // With no series/standalone sections, this is the only pager on the page.
    screen.getByRole("button", { name: "Next" }).click();

    await waitFor(() => {
      const last = getAuthorDetail.mock.calls.at(-1)!;
      expect(last[1]?.missingSeriesOffset).toBe(50);
    });
    expect(await screen.findByText("Matched Series 51")).toBeInTheDocument();
  });

  // --- Management & Settings: matched-source badge, last-refreshed hint, refresh action ---

  it("shows the matched-source badge, last-refreshed hint and a working Refresh Online button", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(
      makeDetail(0, 0, { lastRefreshedAt: "2026-09-01T12:00:00Z" }),
    );
    vi.spyOn(browseApi, "getAuthorFollowStatus").mockResolvedValue({ isFollowed: false });
    vi.spyOn(browseApi, "getAuthorHardcoverMatch").mockResolvedValue({
      sourceId: "123",
      sourceName: "Hardcover",
      sourceUrl: "https://hardcover.app/authors/brandon-sanderson",
    });
    const refresh = vi
      .spyOn(browseApi, "refreshAuthor")
      .mockResolvedValue({ success: true, lastRefreshedAt: "2026-09-19T00:00:00Z" });

    renderWithProviders();

    expect(await screen.findByText("Management & Settings")).toBeInTheDocument();
    expect(await screen.findByText(/Matched to/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Hardcover/ })).toHaveAttribute(
      "href",
      "https://hardcover.app/authors/brandon-sanderson",
    );
    expect(screen.getByText(/Last refreshed from source: 2026-09-01/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Refresh Online" }));

    await waitFor(() => {
      expect(refresh).toHaveBeenCalledWith(7);
    });
  });

  it("shows the not-matched message instead of Refresh Online for an unmatched author", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(makeDetail(0, 0));
    vi.spyOn(browseApi, "getAuthorFollowStatus").mockResolvedValue({ isFollowed: false });
    vi.spyOn(browseApi, "getAuthorHardcoverMatch").mockResolvedValue({});

    renderWithProviders();

    expect(await screen.findByText("Management & Settings")).toBeInTheDocument();
    expect(screen.getByText(/Not matched to an online metadata provider yet/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Refresh Online" })).not.toBeInTheDocument();
  });
});
