import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";

// Same api-mock shape the other full-router tests use; RootLayout itself needs no extra data,
// the /library page it wraps does.
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
    getPendingPage: vi.fn().mockResolvedValue({ items: [], total: 0 }),
    getPendingSummary: vi.fn().mockResolvedValue([]),
    getPendingForAudiobook: vi.fn(),
    dismissPending: vi.fn().mockResolvedValue(undefined),
  },
}));

import { browseApi } from "@/services/api";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";

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
];

describe("RootLayout", () => {
  let queryClient: QueryClient;

  const mockSignalRValue = {
    connection: null,
    isConnected: false,
    on: vi.fn(),
    off: vi.fn(),
    onReconnected: vi.fn(),
    offReconnected: vi.fn(),
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

  it("lists the maintenance tools in order with Metadata Refresh after Missing Tags", async () => {
    const user = userEvent.setup();
    renderWithRouter();

    await user.click(await screen.findByRole("button", { name: /tools/i }));

    const items = await screen.findAllByRole("menuitem");
    expect(items.map((item) => item.textContent)).toEqual([
      "Consistency Check",
      "Missing Tags",
      "Metadata Refresh",
      "Similar Values",
      "Clean Book URLs",
    ]);
  });

  it("navigates to /library/metadata-refresh from the Metadata Refresh item", async () => {
    const user = userEvent.setup();
    const { router } = renderWithRouter();

    await user.click(await screen.findByRole("button", { name: /tools/i }));
    const menu = await screen.findByRole("menu");
    await user.click(await within(menu).findByText("Metadata Refresh"));

    expect(router.state.location.pathname).toBe("/library/metadata-refresh");
  });
});
