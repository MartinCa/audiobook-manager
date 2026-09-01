import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { FolderDeleteContents } from "./FolderDeleteContents";
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

describe("FolderDeleteContents", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });
  it("renders folder path, total size, and file items when fetched via targetPath", async () => {
    vi.mocked(filesApi.getDirectoryContents).mockResolvedValue([
      {
        fullPath: "/audiobooks/Frank Herbert/Dune/Dune.m4b",
        fileName: "Dune.m4b",
        sizeInBytes: 524288000,
      },
      {
        fullPath: "/audiobooks/Frank Herbert/Dune/cover.jpg",
        fileName: "cover.jpg",
        sizeInBytes: 204800,
      },
      {
        fullPath: "/audiobooks/Frank Herbert/Dune/desc.txt",
        fileName: "desc.txt",
        sizeInBytes: 1024,
      },
    ]);

    renderWithClient(<FolderDeleteContents targetPath="/audiobooks/Frank Herbert/Dune/Dune.m4b" />);

    expect(screen.getByText("Folder to be deleted:")).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByTestId("folder-path-display")).toHaveTextContent(
        "/audiobooks/Frank Herbert/Dune",
      );
      expect(screen.getByText("Dune.m4b")).toBeInTheDocument();
      expect(screen.getByText("cover.jpg")).toBeInTheDocument();
      expect(screen.getByText("desc.txt")).toBeInTheDocument();
    });
  });

  it("renders with passed files directly without querying", () => {
    const files = [
      {
        fullPath: "/import/Isaac Asimov/Foundation.m4b",
        fileName: "Foundation.m4b",
        sizeInBytes: 1000,
      },
    ];

    renderWithClient(
      <FolderDeleteContents targetPath="/import/Isaac Asimov/Foundation.m4b" files={files} />,
    );

    expect(screen.getByTestId("folder-path-display")).toHaveTextContent("/import/Isaac Asimov");
    expect(screen.getByText("Foundation.m4b")).toBeInTheDocument();
    expect(filesApi.getDirectoryContents).not.toHaveBeenCalled();
  });

  it("renders empty folder message when files list is empty", async () => {
    vi.mocked(filesApi.getDirectoryContents).mockResolvedValue([]);

    renderWithClient(<FolderDeleteContents targetPath="/audiobooks/EmptyDir" />);

    await waitFor(() => {
      expect(screen.getByTestId("folder-path-display")).toHaveTextContent("/audiobooks/EmptyDir");
      expect(screen.getByText("Folder is empty (0 B)")).toBeInTheDocument();
    });
  });
});
