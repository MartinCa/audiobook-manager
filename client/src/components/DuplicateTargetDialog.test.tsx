import { describe, it, expect, vi, beforeEach } from "vitest";
import { useState } from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DuplicateTargetDialog } from "./DuplicateTargetDialog";
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

describe("DuplicateTargetDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders new and existing file paths and triggers callbacks", async () => {
    const onReplaceExisting = vi.fn();
    const onDeleteNew = vi.fn();
    const onOpenChange = vi.fn();

    vi.mocked(filesApi.getDirectoryContents).mockResolvedValue([
      { fullPath: "/staging/audiobook.m4b", fileName: "audiobook.m4b", sizeInBytes: 50000000 },
    ]);

    renderWithClient(
      <DuplicateTargetDialog
        open={true}
        onOpenChange={onOpenChange}
        newPath="/staging/audiobook.m4b"
        newSizeInBytes={50000000}
        targetPath="/library/Author/audiobook.m4b"
        existingSizeInBytes={60000000}
        onReplaceExisting={onReplaceExisting}
        onDeleteNew={onDeleteNew}
      />,
    );

    expect(screen.getByText("/staging/audiobook.m4b")).toBeInTheDocument();
    expect(screen.getByText("/library/Author/audiobook.m4b")).toBeInTheDocument();

    const replaceBtn = screen.getByText("Replace existing");
    fireEvent.click(replaceBtn);
    expect(onReplaceExisting).toHaveBeenCalledTimes(1);

    // Deleting now opens directory contents preview & confirmation
    const deleteBtn = screen.getByText("Delete new file");
    fireEvent.click(deleteBtn);

    await waitFor(() => {
      expect(screen.getByText("Confirm Deletion of New File")).toBeInTheDocument();
      expect(screen.getByTestId("folder-path-display")).toHaveTextContent("/staging");
    });

    const confirmBtn = screen.getByText("Confirm Delete");
    fireEvent.click(confirmBtn);
    expect(onDeleteNew).toHaveBeenCalledTimes(1);
  });

  it("resets the confirmation view when closed and reopened", async () => {
    const onDeleteNew = vi.fn();

    function ControlledDialog() {
      const [open, setOpen] = useState(true);
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Reopen duplicate dialog
          </button>
          <DuplicateTargetDialog
            open={open}
            onOpenChange={setOpen}
            newPath="/staging/audiobook.m4b"
            newSizeInBytes={50000000}
            targetPath="/library/Author/audiobook.m4b"
            onReplaceExisting={vi.fn()}
            onDeleteNew={onDeleteNew}
          />
        </>
      );
    }

    vi.mocked(filesApi.getDirectoryContents).mockResolvedValue([]);
    renderWithClient(<ControlledDialog />);

    fireEvent.click(screen.getByRole("button", { name: "Delete new file" }));
    expect(await screen.findByText("Confirm Deletion of New File")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Confirm Delete" }));
    await waitFor(() => expect(onDeleteNew).toHaveBeenCalledOnce());

    expect(screen.queryByText("Confirm Deletion of New File")).not.toBeInTheDocument();

    // Reopening the same mounted dialog must start at the comparison view for the next book.
    fireEvent.click(screen.getByRole("button", { name: "Reopen duplicate dialog" }));
    expect(screen.getByRole("button", { name: "Delete new file" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Delete new file" }));
    expect(screen.getByText("Confirm Deletion of New File")).toBeInTheDocument();
  });

  it("does not render Delete new file button if onDeleteNew is not provided", () => {
    renderWithClient(
      <DuplicateTargetDialog
        open={true}
        onOpenChange={vi.fn()}
        newPath="/staging/audiobook.m4b"
        newSizeInBytes={50000000}
        targetPath="/library/Author/audiobook.m4b"
        onReplaceExisting={vi.fn()}
      />,
    );

    expect(screen.queryByText("Delete new file")).not.toBeInTheDocument();
    expect(screen.getByText("Replace existing")).toBeInTheDocument();
    expect(screen.getByText("Cancel")).toBeInTheDocument();
  });
});
