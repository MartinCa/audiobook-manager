import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DeleteFileDialog } from "./DeleteFileDialog";
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

describe("DeleteFileDialog", () => {
  it("renders folder contents and invokes onConfirmDelete when confirmed", async () => {
    const onConfirmDelete = vi.fn().mockResolvedValue(undefined);
    const onOpenChange = vi.fn();

    vi.mocked(filesApi.getDirectoryContents).mockResolvedValue([
      { fullPath: "/staging/book/track1.mp3", fileName: "track1.mp3", sizeInBytes: 1048576 },
      { fullPath: "/staging/book/cover.jpg", fileName: "cover.jpg", sizeInBytes: 204800 },
    ]);

    renderWithClient(
      <DeleteFileDialog
        open={true}
        onOpenChange={onOpenChange}
        targetPath="/staging/book"
        onConfirmDelete={onConfirmDelete}
      />,
    );

    expect(screen.getByText("Delete File")).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText("track1.mp3")).toBeInTheDocument();
      expect(screen.getByText("cover.jpg")).toBeInTheDocument();
    });

    const confirmBtn = screen.getByText("Delete Permanently");
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(onConfirmDelete).toHaveBeenCalledTimes(1);
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
  });
});
