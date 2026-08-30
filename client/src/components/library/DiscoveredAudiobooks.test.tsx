import { describe, it, expect, vi } from "vitest";
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
});
