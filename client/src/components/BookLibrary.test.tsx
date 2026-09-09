import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";

vi.mock("@/services/api", () => ({
  browseApi: {
    getAudiobooks: vi.fn(),
    searchAudiobooks: vi.fn(),
    getCoverUrl: vi.fn((id: number) => `/api/browse/audiobooks/${id}/cover`),
    getAuthors: vi.fn().mockResolvedValue([]),
    getAudiobookDetail: vi.fn(),
  },
  consistencyApi: {
    getIssues: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }),
    // The library list reads the per-audiobook summary now rather than every issue.
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

import { browseApi, metadataRefreshApi } from "@/services/api";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";
import type { AudiobookDetail } from "@/types/AudiobookDetail";

describe("BookLibrary", () => {
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
      bookName: "The Way of Kings",
      year: 2010,
      authors: ["Brandon Sanderson"],
      series: "The Stormlight Archive",
      seriesPart: "1",
      narrators: ["Michael Kramer", "Kate Reading"],
      genres: ["Fantasy"],
      durationInSeconds: 164000,
      coverFilePath: "/covers/1.jpg",
    },
    {
      id: 2,
      bookName: "Words of Radiance",
      year: 2014,
      authors: ["Brandon Sanderson"],
      series: "The Stormlight Archive",
      seriesPart: "2",
      narrators: ["Michael Kramer", "Kate Reading"],
      genres: ["Fantasy"],
      durationInSeconds: 170000,
      coverFilePath: "/covers/2.jpg",
    },
  ];

  const sampleDetail: AudiobookDetail = {
    id: 1,
    bookName: "The Way of Kings",
    subtitle: null,
    series: "The Stormlight Archive",
    seriesPart: "1",
    year: 2010,
    authors: ["Brandon Sanderson"],
    narrators: ["Michael Kramer", "Kate Reading"],
    genres: ["Fantasy"],
    description: "Sample",
    copyright: null,
    publisher: "Tor",
    language: "eng",
    rating: "4.8",
    asin: "B123",
    www: "https://example.com",
    filePath: "/path/book.m4b",
    fileName: "book.m4b",
    sizeInBytes: 1000,
    durationInSeconds: 164000,
    coverFilePath: "/covers/1.jpg",
  };

  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    vi.mocked(browseApi.getAudiobooks).mockResolvedValue({
      items: sampleBooks,
      count: sampleBooks.length,
      total: sampleBooks.length,
    });
    vi.mocked(browseApi.searchAudiobooks).mockResolvedValue({
      items: [sampleBooks[0]!],
      count: 1,
      total: 1,
    });
  });

  function renderWithRouter(initialEntry = "/library") {
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

  it("loads and displays all audiobooks by default", async () => {
    renderWithRouter();

    expect(await screen.findByText("The Way of Kings")).toBeInTheDocument();
    expect(screen.getByText("Words of Radiance")).toBeInTheDocument();
    expect(browseApi.getAudiobooks).toHaveBeenCalledWith(20, 0);
  });

  it("shows a pending-refresh badge for books with a stored snapshot", async () => {
    vi.mocked(metadataRefreshApi.getPendingSummary).mockResolvedValue([1]);

    renderWithRouter();

    // The summary is folded into the list query, so the badge appears on the matching row only.
    expect(await screen.findByText("Pending refresh")).toBeInTheDocument();
    expect(screen.getByText("The Way of Kings").closest("div")!.textContent).toContain(
      "Pending refresh",
    );
    expect(screen.getByText("Words of Radiance").closest("div")!.textContent).not.toContain(
      "Pending refresh",
    );
  });

  it("fits the whole cover inside the thumbnail without cropping", async () => {
    renderWithRouter();

    const img = await screen.findByAltText<HTMLImageElement>("The Way of Kings");
    expect(img).toHaveClass("object-contain");
  });

  it("populates search query and loads search results when initial route has q param", async () => {
    renderWithRouter("/library?q=Kings");

    const input = await screen.findByPlaceholderText(/Search title, author/i);
    expect(input).toHaveValue("Kings");

    expect(await screen.findByText("The Way of Kings")).toBeInTheDocument();
    expect(browseApi.searchAudiobooks).toHaveBeenCalledWith("Kings", 20, 0);
  });

  it("updates URL search query when user types in search input", async () => {
    const user = userEvent.setup();
    const { router } = renderWithRouter();

    const input = await screen.findByPlaceholderText(/Search title, author/i);
    await user.type(input, "Kings");

    await waitFor(() => {
      expect(router.state.location.search).toEqual({ q: "Kings" });
    });

    await waitFor(() => {
      expect(browseApi.searchAudiobooks).toHaveBeenCalledWith("Kings", 20, 0);
    });
  });

  it("preserves trailing spaces in search input while typing", async () => {
    const user = userEvent.setup();
    const { router } = renderWithRouter();

    const input = await screen.findByPlaceholderText(/Search title, author/i);
    await user.type(input, "Brandon ");

    await waitFor(() => {
      expect(router.state.location.search).toEqual({ q: "Brandon" });
    });

    expect(input).toHaveValue("Brandon ");
  });

  it("preserves search results when navigating to book entry and back", async () => {
    vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue(sampleDetail);

    renderWithRouter("/library?q=Kings");

    expect(await screen.findByText("The Way of Kings")).toBeInTheDocument();

    // Click book row to navigate to book detail
    const bookLink = screen.getByRole("link", { name: /The Way of Kings/i });
    expect(bookLink).toHaveAttribute("href", "/library/book/1");
    expect(bookLink).not.toHaveAttribute("target");
    fireEvent.click(bookLink);

    // Expect to be on book detail page with book details loaded
    expect(await screen.findByDisplayValue("The Way of Kings")).toBeInTheDocument();

    // Click Back to Library button
    const backBtn = await screen.findByRole("button", { name: /back to library/i });
    fireEvent.click(backBtn);

    // Expect to return to /library?q=Kings with search input and results intact
    const input = await screen.findByPlaceholderText(/Search title, author/i);
    await waitFor(() => {
      expect(input).toHaveValue("Kings");
    });
    expect(await screen.findByText("The Way of Kings")).toBeInTheDocument();
  });

  it("clears search input and removes query parameter when clear button is clicked", async () => {
    const { router } = renderWithRouter("/library?q=Kings");

    const clearBtn = await screen.findByRole("button", { name: /clear search/i });
    fireEvent.click(clearBtn);

    const input = await screen.findByPlaceholderText(/Search title, author/i);
    expect(input).toHaveValue("");
    expect(router.state.location.search).toEqual({});
  });

  it("places the Books/Series/Authors tabs before the page heading", async () => {
    renderWithRouter();

    expect(await screen.findByText("The Way of Kings")).toBeInTheDocument();

    const tabsList = screen.getByRole("tablist");
    const heading = screen.getByRole("heading", { name: "Library Audiobooks" });

    // The three tab triggers prove the tablist is the Books/Series/Authors switcher, not some
    // other tab widget.
    expect(screen.getAllByRole("tab")).toHaveLength(3);
    expect(tabsList.compareDocumentPosition(heading) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(
      0,
    );
  });

  it("renders the Discovered Files link only in the nav bar", async () => {
    renderWithRouter();

    expect(await screen.findByText("The Way of Kings")).toBeInTheDocument();

    // One match for the nav-bar link; the page-level "Discovered Files" button was removed.
    expect(screen.getAllByText("Discovered Files")).toHaveLength(1);
  });

  it("renders no page-level Tools menu next to the nav dropdown", async () => {
    renderWithRouter();

    expect(await screen.findByText("The Way of Kings")).toBeInTheDocument();

    // The single Tools trigger left on the page belongs to the nav bar; BookLibrary's own
    // LibraryToolsMenu was removed.
    expect(screen.getAllByRole("button", { name: /tools/i })).toHaveLength(1);
  });
});
