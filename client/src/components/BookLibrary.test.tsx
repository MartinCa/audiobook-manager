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
    getFilterOptions: vi.fn().mockResolvedValue({ sources: [], genres: [], languages: [] }),
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
    getAutocomplete: vi.fn().mockResolvedValue([]),
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
    authorRefs: [{ id: 1, name: "Brandon Sanderson" }],
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
    expect(browseApi.getAudiobooks).toHaveBeenCalledWith(20, 0, {});
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
    expect(browseApi.searchAudiobooks).toHaveBeenCalledWith("Kings", 20, 0, {});
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
      expect(browseApi.searchAudiobooks).toHaveBeenCalledWith("Kings", 20, 0, {});
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

  it("returns to the library list through the stable Back to Library link", async () => {
    vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue(sampleDetail);

    const { router } = renderWithRouter("/library?q=Kings");

    expect(await screen.findByText("The Way of Kings")).toBeInTheDocument();

    // Click book row to navigate to book detail
    const bookLink = screen.getByRole("link", { name: /The Way of Kings/i });
    expect(bookLink).toHaveAttribute("href", "/library/book/1");
    expect(bookLink).not.toHaveAttribute("target");
    fireEvent.click(bookLink);

    // Expect to be on the read-only book detail page with book details loaded.
    expect(await screen.findByText(/Brandon Sanderson — The Way of Kings/)).toBeInTheDocument();
    expect(screen.queryByDisplayValue("The Way of Kings")).not.toBeInTheDocument();

    // Back to Library is a real link with a stable href (not history-dependent), so it lands on
    // the library list root rather than replaying the previous URL.
    const backBtn = await screen.findByRole("button", { name: /back to library/i });
    expect(backBtn.tagName).toBe("A");
    expect(backBtn).toHaveAttribute("href", "/library");
    fireEvent.click(backBtn);

    await waitFor(() => {
      expect(router.state.location.search).toEqual({});
    });
    // The full list renders and the search input is cleared along with the dropped query.
    expect(await screen.findByText("The Way of Kings")).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/Search title, author/i)).toHaveValue("");
  });

  it("clears search input and removes query parameter when clear button is clicked", async () => {
    const { router } = renderWithRouter("/library?q=Kings");

    const clearBtn = await screen.findByRole("button", { name: /clear search/i });
    fireEvent.click(clearBtn);

    const input = await screen.findByPlaceholderText(/Search title, author/i);
    expect(input).toHaveValue("");
    expect(router.state.location.search).toEqual({});
  });

  it("places the Books/Series/Authors/Releases tabs before the page heading", async () => {
    renderWithRouter();

    expect(await screen.findByText("The Way of Kings")).toBeInTheDocument();

    const tabsList = screen.getByRole("tablist");
    const heading = screen.getByRole("heading", { name: "Library Audiobooks" });

    // The four tab triggers prove the tablist is the Books/Series/Authors/Releases switcher, not
    // some other tab widget.
    expect(screen.getAllByRole("tab")).toHaveLength(4);
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

  it("tracks row selection and reflects it in the select-all checkbox", async () => {
    renderWithRouter();
    await screen.findByText("The Way of Kings");

    const selectAll = screen.getByRole("checkbox", { name: "Select page" });
    expect(selectAll).toHaveAttribute("aria-checked", "false");

    // Click the two rows' checkboxes: some (but not all) rows picked -> indeterminate.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select The Way of Kings" }));
    expect(selectAll).toHaveAttribute("aria-checked", "mixed");
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Words of Radiance" }));
    expect(selectAll).toHaveAttribute("aria-checked", "true");

    // Deselect one row: back to the mixed state.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select The Way of Kings" }));
    expect(selectAll).toHaveAttribute("aria-checked", "mixed");

    // Deselect the remaining row: page fully cleared.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Words of Radiance" }));
    expect(selectAll).toHaveAttribute("aria-checked", "false");
  });

  it("selects and clears the whole page through the select-all checkbox", async () => {
    renderWithRouter();
    await screen.findByText("The Way of Kings");

    fireEvent.click(screen.getByRole("checkbox", { name: "Select page" }));
    expect(screen.getByRole("checkbox", { name: "Select The Way of Kings" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
    expect(screen.getByRole("checkbox", { name: "Select Words of Radiance" })).toHaveAttribute(
      "aria-checked",
      "true",
    );

    fireEvent.click(screen.getByRole("checkbox", { name: "Select page" }));
    expect(screen.getByRole("checkbox", { name: "Select The Way of Kings" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
  });

  it("keeps the selection when moving to the next page", async () => {
    vi.mocked(browseApi.getAudiobooks).mockResolvedValue({
      items: sampleBooks,
      count: 2,
      total: 40,
    });
    renderWithRouter();
    await screen.findByText("The Way of Kings");

    fireEvent.click(screen.getByRole("checkbox", { name: "Select The Way of Kings" }));

    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    await waitFor(() => {
      expect(browseApi.getAudiobooks).toHaveBeenCalledWith(20, 20, {});
    });

    // The pick survived the page change: book 1's row (rendered again on this mocked page) is
    // still checked, and the new page's select-all reflects a single picked row.
    expect(
      await screen.findByRole("checkbox", { name: "Select The Way of Kings" }),
    ).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "mixed",
    );
  });

  it("shows skeleton loading rows while the library list loads", async () => {
    vi.mocked(browseApi.getAudiobooks).mockImplementation(() => new Promise(() => {}));

    const { container } = renderWithRouter();

    expect(
      await screen.findByRole("status", { name: "Loading library audiobooks..." }),
    ).toBeInTheDocument();
    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0);
  });
});
