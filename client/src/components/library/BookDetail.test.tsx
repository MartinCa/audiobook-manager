import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalREvents } from "@/constants/signalrEvents";
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
    getSeriesPartConflicts: vi.fn().mockResolvedValue({ conflicts: [], truncated: false }),
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
    getAutocomplete: vi.fn().mockResolvedValue([]),
    getEntryStatus: vi.fn().mockResolvedValue({
      value: "",
      status: "new",
      exactMatch: null,
      similarMatches: [],
    }),
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

import {
  browseApi,
  audiobookApi,
  consistencyApi,
  metadataRefreshApi,
  similarValuesApi,
} from "@/services/api";

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
    authorRefs: [{ id: 7, name: "Brandon Sanderson" }],
  };

  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue(sampleBookDetail);
    vi.mocked(audiobookApi.deleteAudiobook).mockResolvedValue();
    vi.mocked(audiobookApi.updateBook).mockResolvedValue();
    vi.mocked(audiobookApi.getSeriesPartConflicts).mockResolvedValue({
      conflicts: [],
      truncated: false,
    });
    vi.mocked(consistencyApi.getIssuesByAudiobook).mockResolvedValue([]);
  });

  function renderWithProviders(path = "/library/book/42") {
    const router = createRouter({
      routeTree,
      history: createMemoryHistory({ initialEntries: [path] }),
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

  // ---- Read-only default route ----

  it("renders the read-only detail page with author and series links by default", async () => {
    renderWithProviders();

    expect(await screen.findByText(/Brandon Sanderson — The Way of Kings/)).toBeInTheDocument();

    // The form is not on the default route.
    expect(screen.queryByDisplayValue("The Way of Kings")).not.toBeInTheDocument();

    // Author link uses the id from the additive authorRefs field.
    const authorLink = screen.getByRole("link", { name: "Brandon Sanderson" });
    expect(authorLink.getAttribute("href")).toBe("/library/authors/7");

    const seriesLink = screen.getByRole("link", { name: "The Stormlight Archive" });
    expect(seriesLink.getAttribute("href")).toBe("/library/series/The%20Stormlight%20Archive");

    expect(screen.getByText("· part 1")).toBeInTheDocument();
    expect(screen.getByText(/An epic fantasy story\./)).toBeInTheDocument();
  });

  it("Edit button navigates to the edit route", async () => {
    const { router } = renderWithProviders();

    const editBtn = await screen.findByRole("button", { name: /edit/i });
    fireEvent.click(editBtn);

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/library/book/42/edit");
    });
  });

  // The read-only page is a view, not an editor: every control on it must be a link or a
  // diagnostic, never one that mutates the book or its stored state. The editor on the /edit
  // route keeps those controls.
  it("read-only page exposes no mutation controls", async () => {
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
    vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue({
      audiobookId: 42,
      fetchedAt: "2026-09-01T10:00:00Z",
      sourceName: "Goodreads",
      sourceUrl: "https://example.com/book",
      payload: {
        url: "https://example.com/book",
        source: "Goodreads",
        authors: ["Brandon Sanderson"],
        bookName: "The Way of Kings",
      },
    });

    renderWithProviders();

    expect(await screen.findByText(/Brandon Sanderson — The Way of Kings/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /edit/i })).toBeInTheDocument();

    expect(screen.queryByRole("button", { name: /save changes/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /delete audiobook/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /refresh now/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /resolve/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /recheck/i })).not.toBeInTheDocument();
  });

  it("edit route retains the mutation controls", async () => {
    renderWithProviders("/library/book/42/edit");

    await screen.findByDisplayValue("The Way of Kings");

    expect(screen.getByRole("button", { name: /save changes/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /delete audiobook/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /refresh now/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /recheck/i })).toBeInTheDocument();
  });

  it("navigates to /library fallback when Back to Library is clicked on direct landing", async () => {
    const { router } = renderWithProviders();

    const backBtn = await screen.findByRole("button", { name: /back to library/i });
    fireEvent.click(backBtn);

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/library");
    });
  });

  // ---- Edit route ----

  it("renders the edit form with book metadata on the /edit route", async () => {
    renderWithProviders("/library/book/42/edit");

    expect(await screen.findByDisplayValue("The Way of Kings")).toBeInTheDocument();
    expect(screen.getByText("Brandon Sanderson")).toBeInTheDocument();
    expect(screen.getByDisplayValue("The Stormlight Archive")).toBeInTheDocument();
    expect(screen.getByDisplayValue("2010")).toBeInTheDocument();
  });

  it("deletes audiobook using audiobookApi.deleteAudiobook with database removal", async () => {
    renderWithProviders("/library/book/42/edit");

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

    renderWithProviders("/library/book/42/edit");

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

    renderWithProviders("/library/book/42/edit");

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

    renderWithProviders("/library/book/42/edit");

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
    // Read-only page: Review Changes is a link into the editor, and Dismiss is a mutation
    // control the read-only page deliberately does not expose.
    expect(screen.getByRole("link", { name: /review changes/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
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

    renderWithProviders("/library/book/42/edit");

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

    renderWithProviders("/library/book/42/edit");

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

    renderWithProviders("/library/book/42/edit");

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

    renderWithProviders("/library/book/42/edit");

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
    handlerFor(SignalREvents.AudiobookSaveComplete)({ audiobookId: 42 } as never);

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

    renderWithProviders("/library/book/42/edit");

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
    handlerFor(SignalREvents.AudiobookSaveComplete)({ audiobookId: 42 } as never);

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

    renderWithProviders("/library/book/42/edit");
    await screen.findByText(/pending metadata changes from goodreads/i);

    // An ordinary save (no pending-refresh marker) completes...
    const handlerFor = (event: string) => {
      const call = mockSignalRValue.on.mock.calls.find(([name]) => name === event);
      expect(call, `a ${event} handler was registered`).toBeDefined();
      return call![1] as (data: never) => void;
    };
    handlerFor(SignalREvents.AudiobookSaveComplete)({ audiobookId: 42 } as never);

    // ...and completes again later; neither should dismiss the untouched pending snapshot.
    handlerFor(SignalREvents.AudiobookSaveComplete)({ audiobookId: 42 } as never);

    await waitFor(() => {
      expect(audiobookApi.updateBook).not.toHaveBeenCalled();
    });
    expect(metadataRefreshApi.dismissPending).not.toHaveBeenCalled();
  });

  // Regression test for Issue A: the old apply built and saved an Audiobook directly in BookDetail
  // (applyPendingRefreshSelection), bypassing the mounted form, so the form kept showing the stale
  // pre-apply values after the background save completed and the detail query refetched. The apply
  // must land in the mounted form's own state, so the form displays the applied series the moment
  // Apply All is clicked - before any save-complete SignalR event.
  it("shows the pending snapshot's series in the mounted form immediately when Apply All is clicked", async () => {
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
        seriesName: "Different Series",
        seriesPart: "5",
        genres: ["Epic Fantasy"],
      },
    });

    renderWithProviders("/library/book/42/edit");

    // The book starts with the old series value.
    expect(await screen.findByDisplayValue("The Stormlight Archive")).toBeInTheDocument();

    const reviewBtn = screen.getByRole("button", { name: /review changes/i });
    fireEvent.click(reviewBtn);
    const applyBtn = await screen.findByRole("button", { name: /apply all/i });
    fireEvent.click(applyBtn);

    // The form itself now reflects the applied snapshot - no save-complete event needed.
    expect(await screen.findByDisplayValue("Different Series")).toBeInTheDocument();
    expect(screen.queryByDisplayValue("The Stormlight Archive")).not.toBeInTheDocument();
  });

  // Regression test for the clearing semantics of a pending apply: the old
  // applyPendingRefreshSelection only copied narrators when the source reported a non-empty list
  // (result.narrators.length > 0), so a book whose source has no narrators kept the stale pair.
  // The routed-through-form apply must honor an empty source array, so the save carries [].
  it("clears narrators in the applying save when the pending snapshot reports none", async () => {
    vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue({
      audiobookId: 42,
      fetchedAt: "2026-09-01T10:00:00Z",
      sourceName: "Goodreads",
      sourceUrl: "https://example.com/book",
      payload: {
        url: "https://example.com/book",
        source: "Goodreads",
        authors: ["Brandon Sanderson"],
        narrators: [],
        bookName: "The Way of Kings",
        genres: ["Epic Fantasy"],
      },
    });

    renderWithProviders("/library/book/42/edit");

    // The book initially carries two narrators.
    expect(await screen.findByText("Michael Kramer")).toBeInTheDocument();

    const reviewBtn = screen.getByRole("button", { name: /review changes/i });
    fireEvent.click(reviewBtn);
    const applyBtn = await screen.findByRole("button", { name: /apply all/i });
    fireEvent.click(applyBtn);

    // The auto-submitted apply save must carry the cleared narrators, not the stale two.
    await waitFor(() => {
      expect(audiobookApi.updateBook).toHaveBeenCalledWith(
        42,
        expect.objectContaining({ narrators: [] }),
      );
    });
  });

  it("warns about series-part conflicts via normal anchor links to the conflicting books", async () => {
    vi.mocked(audiobookApi.getSeriesPartConflicts).mockResolvedValue({
      conflicts: [{ audiobookId: 99, bookName: "Words of Radiance", seriesPart: "2" }],
      truncated: false,
    });

    renderWithProviders("/library/book/42/edit");

    // The edit form's series + part load from the book, so the advisory check runs.
    await waitFor(() => {
      expect(audiobookApi.getSeriesPartConflicts).toHaveBeenCalledWith(
        42,
        "The Stormlight Archive",
        "1",
      );
    });

    expect(
      await screen.findByText(/another book already uses this series part/i),
    ).toBeInTheDocument();
    const conflictLink = screen.getByRole("link", { name: /Words of Radiance/i });
    expect(conflictLink.getAttribute("href")).toBe("/library/book/99");
    expect(conflictLink.getAttribute("target")).toBe("_blank");
    expect(conflictLink.getAttribute("rel")).toBe("noopener noreferrer");
    expect(screen.getByText(/saving is still allowed/i)).toBeInTheDocument();
  });

  it("uses the server-backed entry status for author and series indicators", async () => {
    renderWithProviders("/library/book/42/edit");

    const author = await screen.findByText("Brandon Sanderson");
    expect(author).toBeInTheDocument();

    await waitFor(() => {
      expect(similarValuesApi.getEntryStatus).toHaveBeenCalledWith(
        "author",
        "Brandon Sanderson",
        3,
      );
    });
    await waitFor(() => {
      expect(similarValuesApi.getEntryStatus).toHaveBeenCalledWith(
        "series",
        "The Stormlight Archive",
        3,
      );
    });
  });
});
