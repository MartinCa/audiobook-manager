import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, act, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BookList } from "./BookList";
import { SignalREvents } from "@/constants/signalrEvents";
import { SignalRContext } from "@/context/SignalRContext";
import type { BookFileInfo } from "@/types/BookFileInfo";

vi.mock("@/services/api", () => ({
  untaggedApi: {
    getUntagged: vi.fn(),
  },
  queueApi: {
    getQueuedBooks: vi.fn().mockResolvedValue([]),
  },
  audiobookApi: {
    parseBookDetails: vi.fn(),
    organizeBook: vi.fn(),
    checkTargetPath: vi.fn(),
    generateNewPath: vi.fn(),
  },
  settingsApi: {
    getLanguages: vi.fn().mockResolvedValue({ languages: [] }),
  },
  filesApi: {
    getCoverUrl: vi.fn((path: string) => `/api/files/cover?path=${encodeURIComponent(path)}`),
  },
}));

import { untaggedApi, queueApi, audiobookApi } from "@/services/api";

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
        <BookList />
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
}

const sampleBook: BookFileInfo = {
  fullPath: "/data/import/Sanderson/The Way of Kings.m4b",
  fileName: "The Way of Kings.m4b",
  sizeInBytes: 104857600,
  queueId: undefined,
  queueMessage: undefined,
  queueProgress: undefined,
};

describe("BookList", () => {
  beforeEach(() => {
    queryClient.clear();
    capturedSignalRHandlers = {};
    vi.mocked(queueApi.getQueuedBooks).mockResolvedValue([]);
  });

  it("renders untagged books list and updates progress from SignalR", async () => {
    vi.mocked(untaggedApi.getUntagged).mockResolvedValue({
      items: [sampleBook],
      total: 1,
      count: 1,
    });

    renderWithProviders();

    expect(await screen.findByText("The Way of Kings.m4b")).toBeInTheDocument();

    // Send progress update
    act(() => {
      capturedSignalRHandlers[SignalREvents.UpdateProgress]?.({
        originalFileLocation: sampleBook.fullPath,
        progress: 45,
        progressMessage: "Saving tags",
      });
    });

    expect(await screen.findByText(/Saving tags/)).toBeInTheDocument();
    expect(screen.getByText("45%")).toBeInTheDocument();

    // Send completion
    act(() => {
      capturedSignalRHandlers[SignalREvents.UpdateProgress]?.({
        originalFileLocation: sampleBook.fullPath,
        progress: 100,
        progressMessage: "Done",
      });
    });

    await waitFor(() => {
      expect(screen.queryByText(/Saving tags/)).not.toBeInTheDocument();
    });
  });

  it("matches UpdateProgress using pathsEqual when path separators differ", async () => {
    vi.mocked(untaggedApi.getUntagged).mockResolvedValue({
      items: [sampleBook],
      total: 1,
      count: 1,
    });

    renderWithProviders();

    expect(await screen.findByText("The Way of Kings.m4b")).toBeInTheDocument();

    act(() => {
      capturedSignalRHandlers[SignalREvents.UpdateProgress]?.({
        originalFileLocation: "\\data\\import\\Sanderson\\The Way of Kings.m4b",
        progress: 60,
        progressMessage: "Relocating",
      });
    });

    expect(await screen.findByText(/Relocating/)).toBeInTheDocument();
    expect(screen.getByText("60%")).toBeInTheDocument();
  });

  it("shows skeleton loading rows while the untagged list loads", async () => {
    vi.mocked(untaggedApi.getUntagged).mockImplementation(() => new Promise(() => {}));

    const { container } = renderWithProviders();

    expect(
      await screen.findByRole("status", { name: "Loading files from import directory..." }),
    ).toBeInTheDocument();
    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0);
  });

  // Regression: the accordion header used to place the title left block inside the trigger but
  // kept the live status/progress bar as its sibling, so the progress area was dead space - a
  // click (or keyboard activation) there did nothing. The whole header row, status included,
  // must live inside the trigger.
  it("opens the organize form when the live status area of the header is clicked", async () => {
    vi.mocked(untaggedApi.getUntagged).mockResolvedValue({
      items: [sampleBook],
      total: 1,
      count: 1,
    });
    vi.mocked(audiobookApi.parseBookDetails).mockResolvedValue({
      authors: [{ name: "Isaac Asimov" }],
      narrators: [],
      bookName: "Foundation",
      subtitle: undefined,
      series: "Foundation",
      seriesPart: "1",
      year: 1951,
      genres: ["Sci-Fi"],
      description: "Galactic Empire story",
      copyright: undefined,
      publisher: "Gnome Press",
      language: "eng",
      rating: undefined,
      asin: undefined,
      www: undefined,
      fileInfo: {
        fullPath: sampleBook.fullPath,
        fileName: sampleBook.fileName,
        sizeInBytes: sampleBook.sizeInBytes,
      },
      durationInSeconds: 36000,
    });

    renderWithProviders();
    await screen.findByText("The Way of Kings.m4b");

    act(() => {
      capturedSignalRHandlers[SignalREvents.UpdateProgress]?.({
        originalFileLocation: sampleBook.fullPath,
        progress: 45,
        progressMessage: "Saving tags",
      });
    });
    const statusText = await screen.findByText(/Saving tags/);

    // The status text is inside the trigger: clicking it toggles the row open.
    fireEvent.click(statusText);

    await waitFor(() => {
      expect(audiobookApi.parseBookDetails).toHaveBeenCalledWith(sampleBook.fullPath);
    });
    expect(
      await screen.findByRole("button", { name: /organize into library/i }),
    ).toBeInTheDocument();
  });
});
