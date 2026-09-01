import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, act, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BookList } from "./BookList";
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

import { untaggedApi, queueApi } from "@/services/api";

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
      capturedSignalRHandlers["UpdateProgress"]?.({
        originalFileLocation: sampleBook.fullPath,
        progress: 45,
        progressMessage: "Saving tags",
      });
    });

    expect(await screen.findByText(/Saving tags/)).toBeInTheDocument();
    expect(screen.getByText("45 / 100 (45%)")).toBeInTheDocument();

    // Send completion
    act(() => {
      capturedSignalRHandlers["UpdateProgress"]?.({
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
      capturedSignalRHandlers["UpdateProgress"]?.({
        originalFileLocation: "\\data\\import\\Sanderson\\The Way of Kings.m4b",
        progress: 60,
        progressMessage: "Relocating",
      });
    });

    expect(await screen.findByText(/Relocating/)).toBeInTheDocument();
    expect(screen.getByText("60 / 100 (60%)")).toBeInTheDocument();
  });
});
