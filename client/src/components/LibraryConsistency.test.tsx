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

    // Expand the Orphaned Directories accordion so its per-item controls are visible.
    const orphanTrigger = await screen.findByRole("button", { name: /Orphaned Directories/ });
    fireEvent.click(orphanTrigger);

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

    // Expand the Orphaned Directories accordion so its per-item controls are visible.
    const orphanTrigger = await screen.findByRole("button", { name: /Orphaned Directories/ });
    fireEvent.click(orphanTrigger);

    const deleteAllBtn = await screen.findByRole("button", { name: "Delete All 2" });
    fireEvent.click(deleteAllBtn);

    expect(screen.getByText("Delete All Orphaned Directories")).toBeInTheDocument();

    const confirmBtn = screen.getByRole("button", { name: "Delete All" });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(consistencyApi.resolveAllOrphanDirectories).toHaveBeenCalledTimes(1);
    });
  });

  it("opens selective tag-mismatch dialog and resolves with chosen field values", async () => {
    vi.spyOn(consistencyApi, "getIssues").mockResolvedValue([
      {
        id: 42,
        audiobookId: 7,
        bookName: "The Test Book",
        authors: ["Some Author"],
        issueType: "TagMismatch",
        description: 'Tag mismatch: "bookName" differs',
        detectedAt: "2026-09-01T10:00:00Z",
        expectedValue: "The Test Book",
        actualValue: "The Test Book 2",
      },
    ]);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);
    vi.spyOn(consistencyApi, "getTagMismatch").mockResolvedValue([
      { field: "bookName", libraryValue: "The Test Book", fileValue: "The Test Book 2" },
    ]);
    vi.spyOn(consistencyApi, "resolveTagMismatch").mockResolvedValue({
      issueId: 42,
      issueType: "TagMismatch",
      actionTaken: "resolved",
      message: "Tags updated",
    });

    renderWithProviders(<LibraryConsistency />);

    // Expand the "Tag Mismatches" accordion group.
    const trigger = await screen.findByRole("button", { name: /Tag Mismatches/ });
    fireEvent.click(trigger);

    const resolveBtn = await screen.findByRole("button", { name: "Resolve" });
    fireEvent.click(resolveBtn);

    // Dialog loads fields; choose the file value, then apply.
    expect(await screen.findByText("Resolve Tag Mismatch")).toBeInTheDocument();
    const fileRadio = await screen.findByRole("radio", { name: "The Test Book 2" });
    fireEvent.click(fileRadio);

    fireEvent.click(screen.getByRole("button", { name: "Apply Choices" }));

    await waitFor(() => {
      expect(consistencyApi.resolveTagMismatch).toHaveBeenCalledWith(42, {
        bookName: "The Test Book 2",
      });
    });
  });

  it("pages through a large issue group instead of rendering every entry", async () => {
    const manyIssues = Array.from({ length: 120 }, (_, i) => ({
      id: 1000 + i,
      audiobookId: 10 + i,
      bookName: `Book ${i}`,
      authors: ["Some Author"],
      issueType: "TagMismatch",
      description: "m4b tags do not match library metadata: Subtitle",
      detectedAt: "2026-09-01T10:00:00Z",
      expectedValue: `Subtitle: value ${i}`,
      actualValue: `Subtitle: other ${i}`,
    }));
    vi.spyOn(consistencyApi, "getIssues").mockResolvedValue(manyIssues);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);

    renderWithProviders(<LibraryConsistency />);

    // Expand the Tag Mismatches group.
    const trigger = await screen.findByRole("button", { name: /Tag Mismatches/ });
    fireEvent.click(trigger);

    // Only the first page (50 entries) renders; later entries are reached via Next.
    expect(screen.getByText(/Some Author — Book 0$/)).toBeInTheDocument();
    expect(screen.getByText(/Some Author — Book 49$/)).toBeInTheDocument();
    expect(screen.queryByText(/Some Author — Book 50$/)).not.toBeInTheDocument();
    expect(screen.getByText("Showing 1–50 of 120")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(screen.getByText(/Some Author — Book 50$/)).toBeInTheDocument();
    expect(screen.queryByText(/Some Author — Book 0$/)).not.toBeInTheDocument();
    expect(screen.getByText("Showing 51–100 of 120")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(screen.getByText("Showing 101–120 of 120")).toBeInTheDocument();

    // "Select all visible" operates on the current page only: 20 issues on the last page.
    const selectAllCheckbox = screen.getAllByRole("checkbox")[0];
    expect(selectAllCheckbox).toBeDefined();
    if (selectAllCheckbox) fireEvent.click(selectAllCheckbox);
    expect(screen.getByRole("button", { name: "Resolve Selected (20)" })).toBeInTheDocument();

    // Unchecking on this page clears only this page's selections while the group-wide
    // selection count (which still includes later pages) remains accurate.
    fireEvent.click(selectAllCheckbox as HTMLElement);
    expect(screen.getByText("Select all visible (0 selected total)")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Resolve Selected/ })).not.toBeInTheDocument();
  });

  it("keeps a group-wide selection count across pages and resolves the full set", async () => {
    const manyIssues = Array.from({ length: 120 }, (_, i) => ({
      id: 1000 + i,
      audiobookId: 10 + i,
      bookName: `Book ${i}`,
      authors: ["Some Author"],
      issueType: "TagMismatch",
      description: "m4b tags do not match library metadata: Subtitle",
      detectedAt: "2026-09-01T10:00:00Z",
      expectedValue: `Subtitle: value ${i}`,
      actualValue: `Subtitle: other ${i}`,
    }));
    vi.spyOn(consistencyApi, "getIssues").mockResolvedValue(manyIssues);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);
    vi.spyOn(consistencyApi, "resolveSelected").mockResolvedValue({ resolved: 50, failed: 0 });

    renderWithProviders(<LibraryConsistency />);

    const trigger = await screen.findByRole("button", { name: /Tag Mismatches/ });
    fireEvent.click(trigger);

    // Select all 50 on page 1, then move to page 2.
    const selectAllCheckbox = screen.getAllByRole("checkbox")[0];
    expect(selectAllCheckbox).toBeDefined();
    if (selectAllCheckbox) fireEvent.click(selectAllCheckbox);
    expect(screen.getByText("Select all visible (50 selected total)")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    // Page 2 is now visible; page 1's selection is hidden but still counted group-wide,
    // and "Resolve Selected" reflects the whole group (not just the visible page).
    expect(screen.getByText(/Some Author — Book 50$/)).toBeInTheDocument();
    expect(screen.getByText("Select all visible (50 selected total)")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Resolve Selected (50)" })).toBeInTheDocument();

    // Resolving the selected set clears them and hides the button.
    fireEvent.click(screen.getByRole("button", { name: "Resolve Selected (50)" }));
    const confirmBtn = await screen.findByRole("button", { name: "Resolve Selected" });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(consistencyApi.resolveSelected).toHaveBeenCalled();
    });
    await waitFor(() => {
      expect(screen.queryByRole("button", { name: /Resolve Selected/ })).not.toBeInTheDocument();
    });
  });
});
