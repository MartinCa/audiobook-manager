import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";
import type * as SonnerModule from "sonner";
import { toast } from "sonner";

vi.mock("sonner", async (importOriginal) => {
  const actual = await importOriginal<typeof SonnerModule>();
  return {
    ...actual,
    toast: {
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
    },
  };
});

vi.mock("@/services/api", () => ({
  browseApi: {
    getAudiobookDetail: vi.fn(),
    getCoverUrl: vi.fn((id: number) => `/api/browse/audiobooks/${id}/cover`),
  },
  audiobookApi: {
    updateBook: vi.fn(),
    deleteAudiobook: vi.fn(),
    checkTargetPath: vi.fn().mockResolvedValue({ exists: false, targetPath: "/library/Book.m4b" }),
    generateNewPath: vi.fn().mockResolvedValue("Author/Book/Book.m4b"),
  },
  consistencyApi: {
    getIssuesByAudiobook: vi.fn().mockResolvedValue([]),
    resolveIssue: vi.fn(),
    recheckAudiobook: vi.fn().mockResolvedValue([]),
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
    getNarratorNames: vi.fn().mockResolvedValue([]),
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

import { browseApi, audiobookApi, consistencyApi, metadataRefreshApi } from "@/services/api";

describe("BookDetail", () => {
  let queryClient: QueryClient;

  const mockSignalRValue = {
    connection: null,
    isConnected: false,
    on: vi.fn(),
    off: vi.fn(),
    onReconnected: vi.fn(),
    offReconnected: vi.fn(),
  };

  const sampleBookDetail = {
    id: 42,
    bookName: "The Way of Kings",
    subtitle: null,
    series: "The Stormlight Archive",
    seriesPart: "1",
    year: 2010,
    authors: ["Brandon Sanderson"],
    narrators: ["Michael Kramer", "Kate Reading"],
    genres: ["Fantasy"],
    description: "An epic fantasy story.",
    copyright: null,
    publisher: "Tor Books",
    language: "eng",
    rating: "4.8",
    asin: "B003P2WO5E",
    www: "https://example.com",
    filePath: "/library/Brandon Sanderson/The Way of Kings.m4b",
    fileName: "The Way of Kings.m4b",
    sizeInBytes: 1048576000,
    durationInSeconds: 164000,
  };

  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue(sampleBookDetail);
    vi.mocked(audiobookApi.deleteAudiobook).mockResolvedValue();
    vi.mocked(audiobookApi.updateBook).mockResolvedValue();
    vi.mocked(consistencyApi.getIssuesByAudiobook).mockResolvedValue([]);
  });

  function renderWithProviders(bookId = "42") {
    const router = createRouter({
      routeTree,
      history: createMemoryHistory({ initialEntries: [`/library/book/${bookId}`] }),
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

    return { ...result, router };
  }

  it("renders book details form with book metadata", async () => {
    renderWithProviders();

    expect(await screen.findByDisplayValue("The Way of Kings")).toBeInTheDocument();
    expect(screen.getByText("Brandon Sanderson")).toBeInTheDocument();
    expect(screen.getByDisplayValue("The Stormlight Archive")).toBeInTheDocument();
    expect(screen.getByDisplayValue("2010")).toBeInTheDocument();
  });

  it("navigates to /library fallback when Back to Library is clicked on direct landing", async () => {
    const { router } = renderWithProviders();

    const backBtn = await screen.findByRole("button", { name: /back to library/i });
    fireEvent.click(backBtn);

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/library");
    });
  });

  it("deletes audiobook using audiobookApi.deleteAudiobook with database removal", async () => {
    renderWithProviders();

    const deleteTrigger = await screen.findByRole("button", { name: /delete audiobook/i });
    fireEvent.click(deleteTrigger);

    // Confirmation dialog opens
    expect(await screen.findByText(/removes the audiobook directory/i)).toBeInTheDocument();

    const confirmButton = screen.getByRole("button", { name: /delete permanently/i });
    fireEvent.click(confirmButton);

    await waitFor(() => {
      expect(audiobookApi.deleteAudiobook).toHaveBeenCalledWith(42);
    });

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Audiobook deleted from library");
    });
  });

  it("shows informative info toast when media file was found on disk during resolve", async () => {
    vi.mocked(consistencyApi.getIssuesByAudiobook).mockResolvedValue([
      {
        id: 101,
        audiobookId: 42,
        bookName: "The Way of Kings",
        authors: ["Brandon Sanderson"],
        issueType: "MissingMediaFile",
        description: "Media file not found",
        expectedValue: sampleBookDetail.filePath,
        actualValue: null,
        detectedAt: "2026-09-01T10:00:00Z",
      },
    ]);

    vi.mocked(consistencyApi.resolveIssue).mockResolvedValue({
      issueId: 101,
      issueType: "MissingMediaFile",
      actionTaken: "file_recovered",
      message:
        "Media file was found on disk. Preserved audiobook and refreshed consistency status.",
    });

    renderWithProviders();

    const resolveBtn = await screen.findByRole("button", { name: /resolve/i });
    fireEvent.click(resolveBtn);

    await waitFor(() => {
      expect(consistencyApi.resolveIssue).toHaveBeenCalledWith(101);
    });

    await waitFor(() => {
      expect(toast.info).toHaveBeenCalledWith(
        "Media file was found on disk. Preserved audiobook and refreshed consistency status.",
      );
    });
  });

  it("invalidates consistency and books queries after single-book recheck", async () => {
    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

    renderWithProviders();

    const recheckBtn = await screen.findByRole("button", { name: /recheck/i });
    fireEvent.click(recheckBtn);

    await waitFor(() => {
      expect(consistencyApi.recheckAudiobook).toHaveBeenCalledWith(42);
    });

    await waitFor(() => {
      const keys = invalidateSpy.mock.calls.map(([arg]) => arg?.queryKey);
      expect(keys).toContainEqual(["bookDetail", 42]);
      expect(keys).toContainEqual(["consistency"]);
      expect(keys).toContainEqual(["books"]);
    });
  });

  it("invalidates consistency and books queries after resolving an issue", async () => {
    vi.mocked(consistencyApi.getIssuesByAudiobook).mockResolvedValue([
      {
        id: 101,
        audiobookId: 42,
        bookName: "The Way of Kings",
        authors: ["Brandon Sanderson"],
        issueType: "TagMismatch",
        description: "m4b tags do not match library metadata: Year",
        expectedValue: JSON.stringify([{ field: "Year", value: "2010" }]),
        actualValue: JSON.stringify([{ field: "Year", value: "2011" }]),
        detectedAt: "2026-09-01T10:00:00Z",
      },
    ]);
    vi.mocked(consistencyApi.resolveIssue).mockResolvedValue({
      issueId: 101,
      issueType: "TagMismatch",
      actionTaken: "resolved",
      message: "Tags and file path updated.",
    });

    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

    renderWithProviders();

    const resolveBtn = await screen.findByRole("button", { name: /resolve/i });
    fireEvent.click(resolveBtn);

    await waitFor(() => {
      expect(consistencyApi.resolveIssue).toHaveBeenCalledWith(101);
    });

    await waitFor(() => {
      const keys = invalidateSpy.mock.calls.map(([arg]) => arg?.queryKey);
      expect(keys).toContainEqual(["bookDetail", 42]);
      expect(keys).toContainEqual(["consistency"]);
      expect(keys).toContainEqual(["books"]);
    });
  });

  it("shows the pending metadata banner when a snapshot exists for the book", async () => {
    vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue({
      audiobookId: 42,
      fetchedAt: "2026-09-01T10:00:00Z",
      sourceName: "Goodreads",
      sourceUrl: "https://example.com/book",
      payload: {
        url: "https://example.com/book",
        source: "Goodreads",
        authors: ["Brandon Sanderson"],
        narrators: ["Michael Kramer"],
        bookName: "The Way of Kings",
        genres: ["Epic Fantasy"],
      },
    });

    renderWithProviders();

    expect(await screen.findByText(/pending metadata changes from goodreads/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /review changes/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /dismiss/i })).toBeInTheDocument();
  });

  it("dismisses the pending snapshot when Dismiss is clicked", async () => {
    vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue({
      audiobookId: 42,
      fetchedAt: "2026-09-01T10:00:00Z",
      sourceName: "Goodreads",
      sourceUrl: "https://example.com/book",
      payload: {
        url: "https://example.com/book",
        source: "Goodreads",
        authors: ["Brandon Sanderson"],
        narrators: ["Michael Kramer"],
        bookName: "The Way of Kings",
        genres: ["Epic Fantasy"],
      },
    });

    renderWithProviders();

    const dismissBtn = await screen.findByRole("button", { name: /dismiss/i });
    fireEvent.click(dismissBtn);

    await waitFor(() => {
      expect(metadataRefreshApi.dismissPending).toHaveBeenCalledWith(42);
    });
    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Pending metadata changes discarded");
    });
  });

  it("runs Refresh Now and opens the pending review when differences remain", async () => {
    vi.mocked(metadataRefreshApi.refreshAudiobook).mockResolvedValue({
      success: true,
      hasDifferences: true,
      differences: [{ field: "BookName", libraryValue: "Old", sourceValue: "New" }],
      sourceName: "Goodreads",
      error: null,
    });
    // After a refresh with differences a pending snapshot appears.
    vi.mocked(metadataRefreshApi.getPendingForAudiobook)
      .mockResolvedValueOnce(undefined as never) // first (initial) fetch: no snapshot yet
      .mockResolvedValue({
        audiobookId: 42,
        fetchedAt: "2026-09-01T10:00:00Z",
        sourceName: "Goodreads",
        sourceUrl: "https://example.com/book",
        payload: {
          url: "https://example.com/book",
          source: "Goodreads",
          authors: ["Brandon Sanderson"],
          narrators: ["Michael Kramer"],
          bookName: "The Way of Kings",
          genres: ["Epic Fantasy"],
        },
      });

    renderWithProviders();

    const refreshBtn = await screen.findByRole("button", { name: /refresh now/i });
    fireEvent.click(refreshBtn);

    await waitFor(() => {
      expect(metadataRefreshApi.refreshAudiobook).toHaveBeenCalledWith(42);
    });
    // The applied snapshot refetches and the TagPreviewDialog (which doubles as the review step)
    // opens with the diff table.
    expect(await screen.findByText("Metadata Preview & Diff")).toBeInTheDocument();
  });

  it("shows an up-to-date toast when Refresh Now finds no differences", async () => {
    vi.mocked(metadataRefreshApi.refreshAudiobook).mockResolvedValue({
      success: true,
      hasDifferences: false,
      differences: [],
      sourceName: "Goodreads",
      error: null,
    });

    renderWithProviders();

    const refreshBtn = await screen.findByRole("button", { name: /refresh now/i });
    fireEvent.click(refreshBtn);

    await waitFor(() => {
      expect(metadataRefreshApi.refreshAudiobook).toHaveBeenCalledWith(42);
    });
    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Metadata up to date (Goodreads)");
    });
  });

  it("dismisses the pending snapshot only after the applying save completes", async () => {
    vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue({
      audiobookId: 42,
      fetchedAt: "2026-09-01T10:00:00Z",
      sourceName: "Goodreads",
      sourceUrl: "https://example.com/book",
      payload: {
        url: "https://example.com/book",
        source: "Goodreads",
        authors: ["Brandon Sanderson"],
        narrators: ["Michael Kramer"],
        bookName: "The Way of Kings",
        genres: ["Epic Fantasy"],
      },
    });

    renderWithProviders();

    const reviewBtn = await screen.findByRole("button", { name: /review changes/i });
    fireEvent.click(reviewBtn);
    const applyBtn = await screen.findByRole("button", { name: /apply all/i });
    fireEvent.click(applyBtn);

    // Applying starts a save carrying the pending-refresh marker.
    await waitFor(() => {
      expect(audiobookApi.updateBook).toHaveBeenCalledWith(
        42,
        expect.objectContaining({ metadataAppliedFromSearch: true }),
      );
    });

    // Simulate the save completing on the SignalR channel.
    const handlerFor = (event: string) => {
      const call = mockSignalRValue.on.mock.calls.find(([name]) => name === event);
      expect(call, `a ${event} handler was registered`).toBeDefined();
      return call![1] as (data: never) => void;
    };
    handlerFor("AudiobookSaveComplete")({ audiobookId: 42 } as never);

    await waitFor(() => {
      expect(metadataRefreshApi.dismissPending).toHaveBeenCalledWith(42);
    });
  });

  it("never lets a failed or cancelled apply save dismiss the pending snapshot on a later save", async () => {
    vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue({
      audiobookId: 42,
      fetchedAt: "2026-09-01T10:00:00Z",
      sourceName: "Goodreads",
      sourceUrl: "https://example.com/book",
      payload: {
        url: "https://example.com/book",
        source: "Goodreads",
        authors: ["Brandon Sanderson"],
        narrators: ["Michael Kramer"],
        bookName: "The Way of Kings",
        genres: ["Epic Fantasy"],
      },
    });
    // The apply's save queueing fails (network/queue error) before a background save starts.
    vi.mocked(audiobookApi.updateBook).mockRejectedValueOnce(new Error("queue refused"));

    renderWithProviders();

    const reviewBtn = await screen.findByRole("button", { name: /review changes/i });
    fireEvent.click(reviewBtn);
    const applyBtn = await screen.findByRole("button", { name: /apply all/i });
    fireEvent.click(applyBtn);

    await waitFor(() => {
      expect(audiobookApi.updateBook).toHaveBeenCalledTimes(1);
    });
    // The failed attempt must not arm the dismiss-after-completion flow.
    const handlerFor = (event: string) => {
      const call = mockSignalRValue.on.mock.calls.find(([name]) => name === event);
      expect(call, `a ${event} handler was registered`).toBeDefined();
      return call![1] as (data: never) => void;
    };
    handlerFor("AudiobookSaveComplete")({ audiobookId: 42 } as never);

    // A later, unrelated save (from the edit form, no marker) completes; it must not dismiss the
    // snapshot the user never applied.
    expect(metadataRefreshApi.dismissPending).not.toHaveBeenCalled();
  });

  it("never dismisses a pending snapshot after an unrelated manual save", async () => {
    vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue({
      audiobookId: 42,
      fetchedAt: "2026-09-01T10:00:00Z",
      sourceName: "Goodreads",
      sourceUrl: "https://example.com/book",
      payload: {
        url: "https://example.com/book",
        source: "Goodreads",
        authors: ["Brandon Sanderson"],
        narrators: ["Michael Kramer"],
        bookName: "The Way of Kings",
        genres: ["Epic Fantasy"],
      },
    });

    renderWithProviders();
    await screen.findByText(/pending metadata changes from goodreads/i);

    // An ordinary save (no pending-refresh marker) completes...
    const handlerFor = (event: string) => {
      const call = mockSignalRValue.on.mock.calls.find(([name]) => name === event);
      expect(call, `a ${event} handler was registered`).toBeDefined();
      return call![1] as (data: never) => void;
    };
    handlerFor("AudiobookSaveComplete")({ audiobookId: 42 } as never);

    // ...and completes again later; neither should dismiss the untouched pending snapshot.
    handlerFor("AudiobookSaveComplete")({ audiobookId: 42 } as never);

    await waitFor(() => {
      expect(audiobookApi.updateBook).not.toHaveBeenCalled();
    });
    expect(metadataRefreshApi.dismissPending).not.toHaveBeenCalled();
  });
});
