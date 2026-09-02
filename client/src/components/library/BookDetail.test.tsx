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
    getSeriesNames: vi.fn().mockResolvedValue([]),
  },
}));

import { browseApi, audiobookApi, consistencyApi } from "@/services/api";

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
    expect(screen.getByDisplayValue("Brandon Sanderson")).toBeInTheDocument();
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
        description: "m4b tags do not match library metadata",
        expectedValue: "Year: 2010",
        actualValue: "Year: 2011",
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
});
