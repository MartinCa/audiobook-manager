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
    searchLibrary: vi.fn(),
    getAudiobooks: vi.fn().mockResolvedValue({ items: [], count: 0, total: 0 }),
    searchAudiobooks: vi.fn().mockResolvedValue({ items: [], count: 0, total: 0 }),
    searchAuthors: vi.fn().mockResolvedValue({ items: [], count: 0, total: 0 }),
    searchSeries: vi.fn().mockResolvedValue({ items: [], count: 0, total: 0 }),
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
import type { LibrarySearchResult } from "@/types/LibrarySearchResult";

describe("LibrarySearch", () => {
  let queryClient: QueryClient;

  const mockSignalRValue = {
    connection: null,
    isConnected: false,
    on: vi.fn(),
    off: vi.fn(),
    onReconnected: vi.fn(),
    offReconnected: vi.fn(),
  };

  const sampleResults: LibrarySearchResult = {
    books: [
      {
        id: 1,
        bookName: "Mistborn: The Final Empire",
        subtitle: null,
        authors: ["Brandon Sanderson"],
        series: "Mistborn",
        year: 2006,
        coverFilePath: null,
      },
    ],
    authors: [],
    series: [],
  };

  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    vi.mocked(browseApi.searchLibrary).mockResolvedValue(sampleResults);
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

  it("navigates to the combined results page when Enter is pressed with a query", async () => {
    const user = userEvent.setup();
    const { router } = renderWithRouter();

    const input = await screen.findByPlaceholderText(/Quick search books, authors, series/i);
    await user.type(input, "mistborn");
    await user.keyboard("{Enter}");

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/library/search");
    });
    expect(router.state.location.search).toEqual({ q: "mistborn" });
    // The input clears and the dropdown closes after navigating away.
    expect(input).toHaveValue("");
  });

  it("does not navigate on Enter when the query is blank", async () => {
    const user = userEvent.setup();
    const { router } = renderWithRouter();

    const input = await screen.findByPlaceholderText(/Quick search books, authors, series/i);
    await user.click(input);
    await user.keyboard("{Enter}");

    expect(router.state.location.pathname).toBe("/library");
  });

  it("shows a 'See all results' footer once results load and navigates on click", async () => {
    const user = userEvent.setup();
    const { router } = renderWithRouter();

    const input = await screen.findByPlaceholderText(/Quick search books, authors, series/i);
    await user.type(input, "mistborn");

    const footer = await screen.findByText(/See all results for "mistborn"/i);
    fireEvent.click(footer);

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/library/search");
    });
    expect(router.state.location.search).toEqual({ q: "mistborn" });
  });
});
