import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BulkDeleteDirectoriesDialog } from "./BulkDeleteDirectoriesDialog";
import { filesApi } from "@/services/api";

vi.mock("@/services/api", () => ({
  filesApi: {
    getDirectoryContents: vi.fn(),
  },
}));

function renderWithClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("BulkDeleteDirectoriesDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders list of directories and allows expanding to view contained files", async () => {
    vi.mocked(filesApi.getDirectoryContents).mockImplementation((path) => {
      if (path === "/library/Author/Orphan1") {
        return Promise.resolve([
          {
            fullPath: "/library/Author/Orphan1/track.mp3",
            fileName: "track.mp3",
            sizeInBytes: 1048576,
          },
        ]);
      }
      return Promise.resolve([]);
    });

    const onConfirmDelete = vi.fn().mockResolvedValue(undefined);
    const onOpenChange = vi.fn();

    const directories = [
      { id: 1, directoryPath: "/library/Author/Orphan1" },
      { id: 2, directoryPath: "/library/Author/Orphan2" },
    ];

    renderWithClient(
      <BulkDeleteDirectoriesDialog
        open={true}
        onOpenChange={onOpenChange}
        directories={directories}
        onConfirmDelete={onConfirmDelete}
      />,
    );

    expect(screen.getByText("Delete Orphaned Directories")).toBeInTheDocument();
    expect(screen.getByText("/library/Author/Orphan1")).toBeInTheDocument();
    expect(screen.getByText("/library/Author/Orphan2")).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText("1 file (1.00 MB)")).toBeInTheDocument();
      expect(screen.getByText("0 files (0 B)")).toBeInTheDocument();
    });

    // Expand the first accordion item to see contained files
    const trigger = screen.getByText("/library/Author/Orphan1");
    fireEvent.click(trigger);

    await waitFor(() => {
      expect(screen.getByText("track.mp3")).toBeInTheDocument();
    });

    const confirmBtn = screen.getByText("Delete All");
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(onConfirmDelete).toHaveBeenCalledTimes(1);
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
  });
});
