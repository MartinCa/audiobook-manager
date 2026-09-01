import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { LibraryConsistency } from "./LibraryConsistency";
import { SignalRContext } from "@/context/SignalRContext";
import type * as ApiModule from "@/services/api";
import { consistencyApi } from "@/services/api";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";
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

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    filesApi: {
      ...actual.filesApi,
      getDirectoryContents: vi.fn().mockResolvedValue([]),
    },
  };
});

let queryClient: QueryClient;

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

function renderWithProviders(ui: React.ReactElement) {
  return render(
    <SignalRContext.Provider value={mockSignalRValue}>
      <QueryClientProvider client={queryClient}>
        <RouterTestWrapper ui={ui} />
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
}

describe("LibraryConsistency", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
  });

  it("renders run check button and consistency header", async () => {
    vi.spyOn(consistencyApi, "getIssues").mockResolvedValue([]);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);

    renderWithProviders(<LibraryConsistency />);
    expect(await screen.findByText("Run Consistency Check")).toBeInTheDocument();
    expect(screen.getByText("Library Consistency")).toBeInTheDocument();
  });

  it("shows info toast when orphan directory resolution retains directory because audio files exist", async () => {
    vi.spyOn(consistencyApi, "getIssues").mockResolvedValue([]);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([
      {
        id: 10,
        directoryPath: "/media/audiobooks/Author/Orphan",
        detectedAt: "2026-09-01T10:00:00Z",
      },
    ]);
    vi.spyOn(consistencyApi, "resolveOrphanDirectory").mockResolvedValue({
      id: 10,
      directoryPath: "/media/audiobooks/Author/Orphan",
      actionTaken: "retained_has_audio",
      message:
        "Directory now contains audio files; preserved directory on disk and removed from orphan list.",
    });

    renderWithProviders(<LibraryConsistency />);

    const deleteBtn = await screen.findByRole("button", { name: "Delete" });
    fireEvent.click(deleteBtn);

    const confirmBtn = await screen.findByRole("button", { name: "Delete Permanently" });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(consistencyApi.resolveOrphanDirectory).toHaveBeenCalledWith(10);
    });

    await waitFor(() => {
      expect(toast.info).toHaveBeenCalledWith(
        "Directory now contains audio files; preserved directory on disk and removed from orphan list.",
      );
    });
  });

  it("opens bulk delete dialog when Delete All orphans button is clicked and deletes all", async () => {
    vi.spyOn(consistencyApi, "getIssues").mockResolvedValue([]);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([
      {
        id: 10,
        directoryPath: "/media/audiobooks/Author/Orphan1",
        detectedAt: "2026-09-01T10:00:00Z",
      },
      {
        id: 11,
        directoryPath: "/media/audiobooks/Author/Orphan2",
        detectedAt: "2026-09-01T10:00:00Z",
      },
    ]);
    vi.spyOn(consistencyApi, "resolveAllOrphanDirectories").mockResolvedValue({
      resolved: 2,
      failed: 0,
      retained: 0,
    });

    renderWithProviders(<LibraryConsistency />);

    const deleteAllBtn = await screen.findByRole("button", { name: "Delete All 2" });
    fireEvent.click(deleteAllBtn);

    expect(screen.getByText("Delete All Orphaned Directories")).toBeInTheDocument();

    const confirmBtn = screen.getByRole("button", { name: "Delete All" });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(consistencyApi.resolveAllOrphanDirectories).toHaveBeenCalledTimes(1);
    });
  });
});
