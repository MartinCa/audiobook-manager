import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";

vi.mock("@/services/api", () => ({
  browseApi: {
    searchLibrary: vi.fn().mockResolvedValue({ books: [], authors: [], series: [] }),
    getAudiobooks: vi.fn().mockResolvedValue({ items: [], count: 0, total: 0 }),
    searchAudiobooks: vi.fn(),
    searchAuthors: vi.fn(),
    searchSeries: vi.fn(),
    getCoverUrl: vi.fn((id: number) => `/api/browse/audiobooks/${id}/cover`),
    getAuthors: vi.fn().mockResolvedValue([]),
    getAudiobookDetail: vi.fn(),
  },
  consistencyApi: {
    getIssues: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }),
    getIssueSummary: vi.fn().mockResolvedValue({}),
    getIssuesByAudiobook: vi.fn().mockResolvedValue([]),
    getConsistencyStatus: vi.fn().mockResolvedValue({ isRunning: false }),
  },
  libraryApi: {
    getScanStatus: vi.fn().mockResolvedValue({ isRunning: false }),
  },
  operationsApi: {
    getStatus: vi.fn().mockResolvedValue({ isRunning: false }),
  },
  seriesApi: {
    getAllSeries: vi.fn().mockResolvedValue([]),
  },
  audiobookApi: {
    updateBook: vi.fn(),
    deleteAudiobook: vi.fn(),
    checkTargetPath: vi.fn().mockResolvedValue({ exists: false, targetPath: "/library/Book.m4b" }),
    generateNewPath: vi.fn().mockResolvedValue("Author/Book/Book.m4b"),
  },
  filesApi: {
    getCoverUrl: vi.fn((path: string) => `/api/files/cover?path=${encodeURIComponent(path)}`),
    getDirectoryContents: vi.fn().mockResolvedValue([]),
    deleteBook: vi.fn(),
  },
  settingsApi: {
    getLanguages: vi.fn().mockResolvedValue({ languages: [] }),
  },
  similarValuesApi: {
    getAuthorNames: vi.fn().mockResolvedValue([]),
    getSeriesNames: vi.fn().mockResolvedValue([]),
  },
  metadataRefreshApi: {
    refreshAudiobook: vi.fn(),
    startBulkRefresh: vi.fn().mockResolvedValue(undefined),
    getPendingPage: vi.fn(),
    getPendingSummary: vi.fn().mockResolvedValue([]),
    getPendingForAudiobook: vi.fn(),
    dismissPending: vi.fn().mockResolvedValue(undefined),
  },
}));

import { browseApi } from "@/services/api";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";
import type { AuthorSummary } from "@/types/AuthorSummary";
import type { LibrarySeriesHit } from "@/types/LibrarySearchResult";
import type { PaginatedResult } from "@/types/Common";

describe("SearchResultsPage", () => {
  let queryClient: QueryClient;

  const mockSignalRValue = {
    connection: null,
    isConnected: false,
    on: vi.fn(),
    off: vi.fn(),
    onReconnected: vi.fn(),
    offReconnected: vi.fn(),
  };

  const sampleBooks: ManagedAudiobook[] = [
    {
      id: 1,
      bookName: "Mistborn: The Final Empire",
      year: 2006,
      authors: ["Brandon Sanderson"],
      series: "Mistborn",
      seriesPart: "1",
      narrators: ["Michael Kramer"],
      genres: ["Fantasy"],
      durationInSeconds: 45000,
      coverFilePath: null,
    },
  ];

  const sampleAuthors: AuthorSummary[] = [{ id: 1, name: "Brandon Sanderson", bookCount: 12 }];

  const sampleSeries: LibrarySeriesHit[] = [{ name: "Mistborn", bookCount: 7 }];

  function makePage<T>(items: T[], total: number): PaginatedResult<T> {
    return { items, count: items.length, total };
  }

  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    vi.mocked(browseApi.searchLibrary).mockResolvedValue({ books: [], authors: [], series: [] });
    vi.mocked(browseApi.searchAudiobooks).mockResolvedValue(makePage(sampleBooks, 8));
    vi.mocked(browseApi.searchAuthors).mockResolvedValue(makePage(sampleAuthors, 2));
    vi.mocked(browseApi.searchSeries).mockResolvedValue(makePage(sampleSeries, 1));
  });

  function renderWithRouter(initialEntry: string) {
    const history = createMemoryHistory({ initialEntries: [initialEntry] });
    const router = createRouter({
      routeTree,
      history,
    });

    const result = render(
      <ThemeProvider defaultTheme="system" storageKey="theme">
        <SignalRContext.Provider value={mockSignalRValue}>
          <QueryClientProvider client={queryClient}>
            <RouterProvider router={router} />
          </QueryClientProvider>
        </SignalRContext.Provider>
      </ThemeProvider>,
    );

    return { ...result, router, history };
  }

  it("renders a prompt to type a query when q is absent", async () => {
    renderWithRouter("/library/search");

    expect(await screen.findByText("Type a query to search")).toBeInTheDocument();
    expect(browseApi.searchAudiobooks).not.toHaveBeenCalled();
    expect(browseApi.searchAuthors).not.toHaveBeenCalled();
    expect(browseApi.searchSeries).not.toHaveBeenCalled();
  });

  // Regression test: a whitespace-only q (e.g. "?q=%20" or "?q=+") used to reach
  // browseApi.searchAudiobooks unfiltered - the books endpoint only short-circuits on
  // IsNullOrWhiteSpace server-side and falls back to "all audiobooks" - while authors/series
  // (which short-circuit the same way) came back empty, producing a confusing page showing
  // every book. The route schema now trims q, so a whitespace-only value normalizes to "" and
  // is treated as "no query" like the absent-q case above.
  it("treats a whitespace-only q as no query and makes no search calls", async () => {
    renderWithRouter("/library/search?q=%20");

    expect(await screen.findByText("Type a query to search")).toBeInTheDocument();
    expect(browseApi.searchAudiobooks).not.toHaveBeenCalled();
    expect(browseApi.searchAuthors).not.toHaveBeenCalled();
    expect(browseApi.searchSeries).not.toHaveBeenCalled();
  });

  it("renders all three sections with mocked rows and totals on the All tab", async () => {
    renderWithRouter("/library/search?q=mist");

    expect(await screen.findByText("Mistborn: The Final Empire")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /Books \(8\)/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /Authors \(2\)/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /Series \(1\)/i })).toBeInTheDocument();
    expect(screen.getByText("Books (8)", { selector: "h2" })).toBeInTheDocument();
    expect(screen.getByText("Authors (2)", { selector: "h2" })).toBeInTheDocument();
    expect(screen.getByText("Series (1)", { selector: "h2" })).toBeInTheDocument();

    expect(screen.getByText("Brandon Sanderson")).toBeInTheDocument();
    expect(screen.getAllByText("Mistborn").length).toBeGreaterThan(0);

    expect(browseApi.searchAudiobooks).toHaveBeenCalledWith("mist", 5, 0);
    expect(browseApi.searchAuthors).toHaveBeenCalledWith("mist", 5, 0);
    expect(browseApi.searchSeries).toHaveBeenCalledWith("mist", 5, 0);
  });

  it("switches to the books tab when 'View all' is clicked", async () => {
    renderWithRouter("/library/search?q=mist");

    const viewAllBooks = await screen.findByText(/View all 8 books/i);
    fireEvent.click(viewAllBooks);

    await waitFor(() => {
      expect(browseApi.searchAudiobooks).toHaveBeenCalledWith("mist", 20, 0);
    });
    // The books tab is now the active tab, showing the full page-sized list.
    expect(screen.getByRole("tab", { name: /Books \(8\)/i })).toHaveAttribute(
      "aria-selected",
      "true",
    );
  });

  it("updates the page search param via the pager on a per-type tab", async () => {
    vi.mocked(browseApi.searchAudiobooks).mockResolvedValue(makePage(sampleBooks, 45));
    const { router } = renderWithRouter("/library/search?q=mist&tab=books");

    await screen.findByText("Mistborn: The Final Empire");
    expect(browseApi.searchAudiobooks).toHaveBeenCalledWith("mist", 20, 0);

    const nextButton = screen.getByRole("button", { name: "Next" });
    fireEvent.click(nextButton);

    await waitFor(() => {
      expect(router.state.location.search).toMatchObject({ tab: "books", page: 2 });
    });
    await waitFor(() => {
      expect(browseApi.searchAudiobooks).toHaveBeenCalledWith("mist", 20, 20);
    });
  });

  it("renders an empty state per section when a section has no hits", async () => {
    vi.mocked(browseApi.searchAuthors).mockResolvedValue(makePage([], 0));
    renderWithRouter("/library/search?q=zzz");

    expect(await screen.findByText("No authors matched your query.")).toBeInTheDocument();
  });
});
