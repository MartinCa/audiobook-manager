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

  it("dismisses a missing standalone book through the upcoming-releases dismiss-roster endpoint", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(
      makeDetail(0, 0, {
        missingBooks: [
          { id: 1, title: "Warbreaker 2", isIgnored: false, year: 2019, sourceUrl: null },
        ],
      }),
    );
    vi.spyOn(browseApi, "getAuthorFollowStatus").mockResolvedValue({ isFollowed: false });
    vi.spyOn(browseApi, "getAuthorHardcoverMatch").mockResolvedValue({});
    const dismiss = vi
      .spyOn(upcomingReleasesApi, "dismissRosterUpcomingRelease")
      .mockResolvedValue(undefined);

    renderWithProviders();

    await screen.findByText("Missing Books (1)");
    fireEvent.click(screen.getByRole("button", { name: "Missing Books (1)" }));
    fireEvent.click(screen.getByRole("button", { name: "Ignore" }));

    await waitFor(() => {
      expect(dismiss).toHaveBeenCalledWith({ authorId: 7, title: "Warbreaker 2" });
    });
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
