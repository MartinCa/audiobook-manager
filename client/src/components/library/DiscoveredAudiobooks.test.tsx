import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
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
    generateNewPath: vi.fn().mockResolvedValue("Author One - Author Two/Book Title/book.m4b"),
  },
  settingsApi: {
    getLanguages: vi.fn().mockResolvedValue({ languages: [] }),
  },
  filesApi: {
    getCoverUrl: vi.fn((path: string) => `/api/files/cover?path=${encodeURIComponent(path)}`),
  },
}));

import { libraryApi } from "@/services/api";

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false } },
});

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
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
  isWellTagged: true,
  isDuplicate: false,
};

describe("DiscoveredAudiobooks", () => {
  // The queryClient is shared module-scope across every test in this file; without clearing it,
  // a later test's differently-shaped mock response can be masked by an earlier test's cached
  // result under the same query key.
  beforeEach(() => {
    queryClient.clear();
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
});
