import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { LibraryConsistency } from "./LibraryConsistency";
import { SignalRContext } from "@/context/SignalRContext";
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
});
