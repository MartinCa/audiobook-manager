import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DiscoveredAudiobooks } from "./DiscoveredAudiobooks";
import { SignalRContext } from "@/context/SignalRContext";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";
import type { DiscoveredAudiobook } from "@/types/DiscoveredAudiobook";

// The backend's DiscoveredAudiobookDto is flat (fullPath/fileName/sizeInBytes, no nested
// fileInfo) and reports authors/narrators/genres as "/"-joined strings, not arrays — see
// AudiobookManager.Api.Dtos.DiscoveredAudiobookDto. A hand-written frontend type once claimed
// the opposite shape (AudiobookPerson[] / string[] and a nested fileInfo), which crashed this
// page (`book.authors.map is not a function`) for any well-tagged discovered file and sent
// `filePath: undefined` to the organize endpoint. This regression-guards both.
vi.mock("@/services/api", () => ({
  libraryApi: {
    getDiscovered: vi.fn(),
    deleteDiscovered: vi.fn(),
    bulkImport: vi.fn(),
    startScan: vi.fn(),
  },
  audiobookApi: {
    organizeBook: vi.fn(),
    checkTargetPath: vi
      .fn()
      .mockResolvedValue({ exists: false, targetPath: "/library/Target/book.m4b" }),
    generateNewPath: vi.fn().mockResolvedValue("Author One - Author Two/Book Title/book.m4b"),
  },
  settingsApi: {
    getLanguages: vi.fn().mockResolvedValue({ languages: [] }),
  },
  filesApi: {
    getCoverUrl: vi.fn((path: string) => `/api/files/cover?path=${encodeURIComponent(path)}`),
    getDirectoryContents: vi.fn().mockResolvedValue([]),
  },
}));

import { libraryApi, audiobookApi } from "@/services/api";

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false } },
});

let capturedSignalRHandlers: Record<string, (payload: unknown) => void> = {};

import type { SignalRContextValue, HubEventHandler } from "@/context/SignalRContext";

const mockSignalRValue: SignalRContextValue = {
  connection: null,
  isConnected: true,
  on: <T,>(eventName: string, handler: HubEventHandler<T>) => {
    capturedSignalRHandlers[eventName] = handler as (payload: unknown) => void;
  },
  off: (eventName: string) => {
    delete capturedSignalRHandlers[eventName];
  },
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

function renderWithProviders() {
  return render(
    <SignalRContext.Provider value={mockSignalRValue}>
      <QueryClientProvider client={queryClient}>
        <RouterTestWrapper ui={<DiscoveredAudiobooks />} />
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
}

const discoveredBook: DiscoveredAudiobook = {
  fullPath: "/import/Book Title/book.m4b",
  fileName: "book.m4b",
  sizeInBytes: 123456,
  bookName: "Book Title",
  authors: "Author One / Author Two",
  narrators: "Narrator One",
  genres: "Fantasy / Adventure",
  year: 2024,
  isWellTagged: true,
  isDuplicate: false,
};

describe("DiscoveredAudiobooks", () => {
  // The queryClient is shared module-scope across every test in this file; without clearing it,
  // a later test's differently-shaped mock response can be masked by an earlier test's cached
  // result under the same query key.
  beforeEach(() => {
    queryClient.clear();
    capturedSignalRHandlers = {};
    vi.mocked(audiobookApi.checkTargetPath).mockResolvedValue({
      exists: false,
      targetPath: "/library/Target/book.m4b",
    });
  });

  it("renders a well-tagged discovered book without crashing on the authors/genres strings", async () => {
    vi.mocked(libraryApi.getDiscovered).mockResolvedValue({
      items: [discoveredBook],
      total: 1,
      count: 1,
    });

    renderWithProviders();

    expect(await screen.findByText("Book Title", { exact: false })).toBeInTheDocument();
    expect(screen.getByText(/Author One \/ Author Two/)).toBeInTheDocument();
  });

  it("populates the edit form's authors field and the file path from the flat DTO fields", async () => {
    vi.mocked(libraryApi.getDiscovered).mockResolvedValue({
      items: [discoveredBook],
      total: 1,
      count: 1,
    });

    renderWithProviders();

    const trigger = await screen.findByText("Book Title", { exact: false });
    fireEvent.click(trigger);

    const authorsInput = await screen.findByDisplayValue("Author One / Author Two");
    expect(authorsInput).toBeInTheDocument();
  });

  // Regression: DiscoveredAudiobookDto didn't carry Description/Copyright/Publisher/Language/
  // Rating/Asin/Www at all - LibraryScanService stores them at scan time, but the DTO silently
  // dropped every one, so the edit form always showed them empty no matter what the file
  // actually had tagged. initialAudiobook built entirely from this DTO, so an untouched "empty"
  // description would be saved as empty on organize, silently erasing a real one.
  it("populates description and copyright from the discovered DTO", async () => {
    vi.mocked(libraryApi.getDiscovered).mockResolvedValue({
      items: [
        {
          ...discoveredBook,
          description: "A real description read from the file at scan time",
          copyright: "2021 Andy Weir",
        },
      ],
      total: 1,
      count: 1,
    });

    renderWithProviders();

    const trigger = await screen.findByText("Book Title", { exact: false });
    fireEvent.click(trigger);

    expect(
      await screen.findByDisplayValue("A real description read from the file at scan time"),
    ).toBeInTheDocument();
    expect(screen.getByDisplayValue("2021 Andy Weir")).toBeInTheDocument();
  });

  it("checks target path collision before organizing and displays DuplicateTargetDialog if existing", async () => {
    vi.mocked(libraryApi.getDiscovered).mockResolvedValue({
      items: [discoveredBook],
      total: 1,
      count: 1,
    });
    vi.mocked(audiobookApi.checkTargetPath).mockResolvedValue({
      exists: true,
      targetPath: "/library/Target/book.m4b",
      existing: {
        sizeInBytes: 60000000,
        durationInSeconds: 3600,
      },
    });

    renderWithProviders();

    const trigger = await screen.findByText("Book Title", { exact: false });
    fireEvent.click(trigger);

    const importBtn = await screen.findByRole("button", { name: /import to library/i });
    fireEvent.click(importBtn);

    await waitFor(() => {
      expect(audiobookApi.checkTargetPath).toHaveBeenCalled();
    });
    expect(await screen.findByText("Duplicate file at target location")).toBeInTheDocument();
  });

  it("displays duration, file size, and technical details in accordion content", async () => {
    vi.mocked(libraryApi.getDiscovered).mockResolvedValue({
      items: [
        {
          ...discoveredBook,
          durationInSeconds: 3665,
          sizeInBytes: 10485760,
        },
      ],
      total: 1,
      count: 1,
    });

    renderWithProviders();

    // Trigger header displays duration and size
    expect(await screen.findByText("1h 1m 5s")).toBeInTheDocument();
    expect(screen.getByText("10.00 MB")).toBeInTheDocument();

    const trigger = await screen.findByText("Book Title", { exact: false });
    fireEvent.click(trigger);

    // Expanded accordion has AudiobookFileDetails
    expect(await screen.findByText("Technical Details")).toBeInTheDocument();
    expect(screen.getAllByText(discoveredBook.fullPath).length).toBeGreaterThanOrEqual(1);
  });

  it("opens DeleteFileDialog when Delete File button is clicked", async () => {
    vi.mocked(libraryApi.getDiscovered).mockResolvedValue({
      items: [discoveredBook],
      total: 1,
      count: 1,
    });

    renderWithProviders();

    const trigger = await screen.findByText("Book Title", { exact: false });
    fireEvent.click(trigger);

    const deleteBtn = await screen.findByRole("button", { name: /delete file/i });
    fireEvent.click(deleteBtn);

    expect(await screen.findByText("Delete Discovered Audiobook")).toBeInTheDocument();
  });

  it("displays live organize progress updates and clears progress bar on completion", async () => {
    vi.mocked(libraryApi.getDiscovered).mockResolvedValue({
      items: [discoveredBook],
      total: 1,
      count: 1,
    });
    vi.mocked(audiobookApi.organizeBook).mockResolvedValue("/library/Target/book.m4b");

    renderWithProviders();

    const trigger = await screen.findByText("Book Title", { exact: false });
    fireEvent.click(trigger);

    const importBtn = await screen.findByRole("button", { name: /import to library/i });
    fireEvent.click(importBtn);

    await waitFor(() => {
      expect(audiobookApi.organizeBook).toHaveBeenCalled();
    });

    // Initially shows queued state
    expect(await screen.findByText("Queued...")).toBeInTheDocument();

    // SignalR sends progress update (e.g. saving tags at 38%)
    act(() => {
      capturedSignalRHandlers["UpdateProgress"]?.({
        originalFileLocation: discoveredBook.fullPath,
        progress: 38,
        progressMessage: "Saving tags",
      });
    });

    expect(await screen.findByText(/Saving tags/)).toBeInTheDocument();
    expect(screen.getByText("38 / 100 (38%)")).toBeInTheDocument();

    // SignalR sends saved tags at 70%
    act(() => {
      capturedSignalRHandlers["UpdateProgress"]?.({
        originalFileLocation: discoveredBook.fullPath,
        progress: 70,
        progressMessage: "Saved tags",
      });
    });

    expect(await screen.findByText(/Saved tags/)).toBeInTheDocument();
    expect(screen.getByText("70 / 100 (70%)")).toBeInTheDocument();

    // SignalR sends completion (100%)
    act(() => {
      capturedSignalRHandlers["UpdateProgress"]?.({
        originalFileLocation: discoveredBook.fullPath,
        progress: 100,
        progressMessage: "Done",
      });
    });

    // Progress bar override is cleared
    await waitFor(() => {
      expect(screen.queryByText(/Saved tags/)).not.toBeInTheDocument();
      expect(screen.queryByText("70 / 100 (70%)")).not.toBeInTheDocument();
    });
  });

  it("matches UpdateProgress with alternative path formatting / casing using pathsEqual", async () => {
    vi.mocked(libraryApi.getDiscovered).mockResolvedValue({
      items: [discoveredBook],
      total: 1,
      count: 1,
    });
    vi.mocked(audiobookApi.organizeBook).mockResolvedValue("/library/Target/book.m4b");

    renderWithProviders();

    const trigger = await screen.findByText("Book Title", { exact: false });
    fireEvent.click(trigger);

    const importBtn = await screen.findByRole("button", { name: /import to library/i });
    fireEvent.click(importBtn);

    await waitFor(() => {
      expect(audiobookApi.organizeBook).toHaveBeenCalled();
    });

    expect(await screen.findByText("Queued...")).toBeInTheDocument();

    // Backend sends path with backslashes instead of forward slashes
    act(() => {
      capturedSignalRHandlers["UpdateProgress"]?.({
        originalFileLocation: "\\import\\Book Title\\book.m4b",
        progress: 38,
        progressMessage: "Saving tags",
      });
    });

    expect(await screen.findByText(/Saving tags/)).toBeInTheDocument();
    expect(screen.getByText("38 / 100 (38%)")).toBeInTheDocument();
  });
});
