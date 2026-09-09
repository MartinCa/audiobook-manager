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

/**
 * Mocks the two calls the component now makes for issues: the per-type counts that size the
 * groups, and one page at a time. Takes the full set the test cares about and serves it the way
 * the server does, so the tests still read as "these issues exist" while asserting the paged
 * contract.
 */
function mockPagedIssues(all: ConsistencyIssueFixture[]) {
  const counts: Record<string, number> = {};
  for (const issue of all) {
    counts[issue.issueType] = (counts[issue.issueType] ?? 0) + 1;
  }

  vi.spyOn(consistencyApi, "getIssueCountsByType").mockResolvedValue(counts);
  vi.spyOn(consistencyApi, "getIssues").mockImplementation((params = {}) => {
    const { issueType, page = 0, pageSize = 50 } = params;
    const matching = issueType ? all.filter((i) => i.issueType === issueType) : all;
    return Promise.resolve({
      items: matching.slice(page * pageSize, (page + 1) * pageSize),
      totalCount: matching.length,
    });
  });
}

type ConsistencyIssueFixture = {
  id: number;
  audiobookId: number;
  bookName: string;
  authors: string[];
  issueType: string;
  description: string;
  detectedAt: string;
  expectedValue?: string;
  actualValue?: string;
};

// Mirrors the backend's TagMismatchPayload serialization.
const tagMismatchPayload = (field: string, value: string) => JSON.stringify([{ field, value }]);

describe("LibraryConsistency", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
  });

  it("renders run check button and consistency header", async () => {
    mockPagedIssues([]);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);

    renderWithProviders(<LibraryConsistency />);
    expect(await screen.findByText("Run Consistency Check")).toBeInTheDocument();
    expect(screen.getByText("Library Consistency")).toBeInTheDocument();
  });

  it("shows info toast when orphan directory resolution retains directory because it is not empty", async () => {
    mockPagedIssues([]);
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
      actionTaken: "retained_not_empty",
      message:
        "Directory still contains files; preserved directory on disk and removed from orphan list.",
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
        "Directory still contains files; preserved directory on disk and removed from orphan list.",
      );
    });
  });

  it("opens bulk delete dialog when Delete All orphans button is clicked and deletes all", async () => {
    mockPagedIssues([]);
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
    mockPagedIssues([
      {
        id: 42,
        audiobookId: 7,
        bookName: "The Test Book",
        authors: ["Some Author"],
        issueType: "TagMismatch",
        description: "m4b tags do not match library metadata: bookName",
        detectedAt: "2026-09-01T10:00:00Z",
        expectedValue: tagMismatchPayload("bookName", "The Test Book"),
        actualValue: tagMismatchPayload("bookName", "The Test Book 2"),
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
      expectedValue: tagMismatchPayload("Subtitle", `value ${i}`),
      actualValue: tagMismatchPayload("Subtitle", `other ${i}`),
    }));
    mockPagedIssues(manyIssues);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);

    renderWithProviders(<LibraryConsistency />);

    // Expand the Tag Mismatches group.
    const trigger = await screen.findByRole("button", { name: /Tag Mismatches/ });
    fireEvent.click(trigger);

    // Only the first page (50 entries) renders; later entries are reached via Next.
    expect(await screen.findByText(/Some Author — Book 0$/)).toBeInTheDocument();
    expect(screen.getByText(/Some Author — Book 49$/)).toBeInTheDocument();
    expect(screen.queryByText(/Some Author — Book 50$/)).not.toBeInTheDocument();
    expect(screen.getByText("Showing 1–50 of 120")).toBeInTheDocument();

    // Each page is now fetched rather than sliced from memory, so the next page arrives
    // asynchronously.
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(await screen.findByText(/Some Author — Book 50$/)).toBeInTheDocument();
    expect(screen.queryByText(/Some Author — Book 0$/)).not.toBeInTheDocument();
    expect(screen.getByText("Showing 51–100 of 120")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(await screen.findByText("Showing 101–120 of 120")).toBeInTheDocument();

    // "Select all visible" operates on the current page only: 20 issues on the last page.
    await screen.findByText(/Some Author — Book 119$/);
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

  // Regression: the page being fetched came straight from pageByType while the pager clamped
  // separately, so a check that shrank a group under the user left the query asking for a page
  // that no longer existed. It came back empty, the pager showed one page, and the group rendered
  // permanently empty with no way to page back into it.
  it("recovers when a refetch shrinks a group below the page the user is on", async () => {
    const manyIssues = Array.from({ length: 120 }, (_, i) => ({
      id: 1000 + i,
      audiobookId: 10 + i,
      bookName: `Book ${i}`,
      authors: ["Some Author"],
      issueType: "TagMismatch",
      description: "m4b tags do not match library metadata: Subtitle",
      detectedAt: "2026-09-01T10:00:00Z",
    }));
    mockPagedIssues(manyIssues);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);

    renderWithProviders(<LibraryConsistency />);

    const trigger = await screen.findByRole("button", { name: /Tag Mismatches/ });
    fireEvent.click(trigger);
    await screen.findByText(/Some Author — Book 0$/);

    // Move to page 2, then have the next fetch return a group of two.
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    await screen.findByText(/Some Author — Book 50$/);

    mockPagedIssues(manyIssues.slice(0, 2));
    await queryClient.invalidateQueries({ queryKey: ["consistency"] });

    // The surviving issues are shown rather than an empty group: the fetch is clamped to the
    // group's last real page, not left pointing past the end.
    expect(await screen.findByText(/Some Author — Book 0$/)).toBeInTheDocument();
    expect(screen.getByText(/Some Author — Book 1$/)).toBeInTheDocument();
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
      expectedValue: tagMismatchPayload("Subtitle", `value ${i}`),
      actualValue: tagMismatchPayload("Subtitle", `other ${i}`),
    }));
    mockPagedIssues(manyIssues);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);
    // Fire-and-forget: the endpoint returns no result body, so the mock resolves void.
    vi.spyOn(consistencyApi, "resolveSelected").mockResolvedValue(undefined);

    renderWithProviders(<LibraryConsistency />);

    const trigger = await screen.findByRole("button", { name: /Tag Mismatches/ });
    fireEvent.click(trigger);

    // Select all 50 on page 1, then move to page 2.
    await screen.findByText(/Some Author — Book 0$/);
    const selectAllCheckbox = screen.getAllByRole("checkbox")[0];
    expect(selectAllCheckbox).toBeDefined();
    if (selectAllCheckbox) fireEvent.click(selectAllCheckbox);
    expect(screen.getByText("Select all visible (50 selected total)")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    // Page 2 is now visible; page 1's selection is hidden but still counted group-wide,
    // and "Resolve Selected" reflects the whole group (not just the visible page). This is the
    // property that needed the selection to carry each issue's type: page 1's issues are no
    // longer in memory to look it up from.
    expect(await screen.findByText(/Some Author — Book 50$/)).toBeInTheDocument();
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

  // Regression: confirming a bulk resolve used to await the whole batch in the dialog, sitting
  // on a "Resolving..." button with no progress for as long as the server rewrote tags. The
  // endpoint is now fire-and-forget, so the dialog closes as soon as the start request returns.
  it("closes the confirmation dialog as soon as a bulk resolve has started", async () => {
    mockPagedIssues([
      {
        id: 7,
        audiobookId: 3,
        bookName: "Some Book",
        authors: ["Some Author"],
        issueType: "MissingDescTxt",
        description: "desc.txt missing",
        detectedAt: "2026-09-01T10:00:00Z",
      },
    ]);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);
    // Fire-and-forget: the endpoint returns no result body, so the mock resolves void.
    vi.spyOn(consistencyApi, "resolveByType").mockResolvedValue(undefined);

    renderWithProviders(<LibraryConsistency />);

    const trigger = await screen.findByRole("button", { name: /Missing Description Files/ });
    fireEvent.click(trigger);

    fireEvent.click(await screen.findByRole("button", { name: "Resolve All 1" }));
    expect(await screen.findByText("Confirm Resolution")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Resolve All" }));

    await waitFor(() => {
      expect(consistencyApi.resolveByType).toHaveBeenCalledWith("MissingDescTxt");
    });
    await waitFor(() => {
      expect(screen.queryByText("Confirm Resolution")).not.toBeInTheDocument();
    });
    expect(toast.success).toHaveBeenCalledWith(
      'Resolution started for all "Missing Description Files" issues',
    );
  });

  // Regression: with no disabled guard on the confirm button, a double-click fired
  // confirmPendingResolve twice, the second start hit the server's 409, and the user got a
  // spurious "already in progress" error on a resolve that started fine.
  it("sends only one start request when the confirm button is clicked twice", async () => {
    mockPagedIssues([
      {
        id: 7,
        audiobookId: 3,
        bookName: "Some Book",
        authors: ["Some Author"],
        issueType: "MissingDescTxt",
        description: "desc.txt missing",
        detectedAt: "2026-09-01T10:00:00Z",
      },
    ]);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);
    let releaseStart!: () => void;
    const startGate = new Promise<void>((resolve) => {
      releaseStart = resolve;
    });
    vi.spyOn(consistencyApi, "resolveByType").mockReturnValue(startGate);

    renderWithProviders(<LibraryConsistency />);

    fireEvent.click(await screen.findByRole("button", { name: /Missing Description Files/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Resolve All 1" }));
    await screen.findByText("Confirm Resolution");

    const confirmBtn = screen.getByRole("button", { name: "Resolve All" });
    // Two clicks before the first start request settles must not produce two requests.
    fireEvent.click(confirmBtn);
    fireEvent.click(confirmBtn);

    releaseStart();
    await waitFor(() => {
      expect(screen.queryByText("Confirm Resolution")).not.toBeInTheDocument();
    });
    expect(consistencyApi.resolveByType).toHaveBeenCalledTimes(1);
  });

  it("keeps the confirmation dialog open and toasts the error when a bulk resolve is refused", async () => {
    mockPagedIssues([
      {
        id: 7,
        audiobookId: 3,
        bookName: "Some Book",
        authors: ["Some Author"],
        issueType: "MissingDescTxt",
        description: "desc.txt missing",
        detectedAt: "2026-09-01T10:00:00Z",
      },
    ]);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);
    // The server refuses a second bulk resolve with 409 while one is in progress.
    vi.spyOn(consistencyApi, "resolveByType").mockRejectedValue(
      new Error("An operation is already in progress."),
    );

    renderWithProviders(<LibraryConsistency />);

    const trigger = await screen.findByRole("button", { name: /Missing Description Files/ });
    fireEvent.click(trigger);

    fireEvent.click(await screen.findByRole("button", { name: "Resolve All 1" }));
    await screen.findByText("Confirm Resolution");

    // The dialog stays open for the user to retry or cancel.
    fireEvent.click(screen.getByRole("button", { name: "Resolve All" }));

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalled();
    });
    expect(screen.getByText("Confirm Resolution")).toBeInTheDocument();
  });

  // The progress the dialog used to hide now renders on the page, driven by the SignalR events
  // the background resolve broadcasts.
  it("shows resolve progress from SignalR and refreshes the issues on completion", async () => {
    mockPagedIssues([
      {
        id: 7,
        audiobookId: 3,
        bookName: "Some Book",
        authors: ["Some Author"],
        issueType: "MissingDescTxt",
        description: "desc.txt missing",
        detectedAt: "2026-09-01T10:00:00Z",
      },
    ]);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);

    renderWithProviders(<LibraryConsistency />);
    const trigger = await screen.findByRole("button", { name: /Missing Description Files/ });
    fireEvent.click(trigger);
    await screen.findByRole("button", { name: "Resolve All 1" });

    // useSignalREvent registered the component's handlers through the mocked context.
    const handlerFor = (event: string) => {
      const call = mockSignalRValue.on.mock.calls.find(([name]) => name === event);
      expect(call, `a ${event} handler was registered`).toBeDefined();
      return call![1] as (data: never) => void;
    };

    handlerFor("ConsistencyResolveProgress")({
      processed: 2,
      total: 5,
      succeeded: 2,
      failed: 0,
    } as never);

    expect(await screen.findByText("Resolving issues (2 resolved, 0 failed)")).toBeInTheDocument();
    // The group buttons are disabled while the server-side gate holds a resolve.
    expect(screen.getByRole("button", { name: "Resolve All 1" })).toBeDisabled();

    handlerFor("ConsistencyResolveComplete")({
      totalProcessed: 5,
      totalSucceeded: 5,
      totalFailed: 0,
    } as never);

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Resolved 5 issues (0 failed)");
    });
    expect(screen.queryByText(/Resolving issues/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Resolve All 1" })).toBeEnabled();
  });

  // The selection bar's re-check (consistency-check-selected) broadcasts the same
  // ConsistencyCheckProgress/Complete events as the full library check. The full-check page must
  // ignore the selected-scope events, or a live few-books re-check would spin up the full check's
  // progress UI and toast a "Check complete: N books checked" that the user never asked for.
  it("ignores a selected-scope consistency event and never toasts or flips the checking state", async () => {
    mockPagedIssues([]);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);

    renderWithProviders(<LibraryConsistency />);
    expect(await screen.findByText("Run Consistency Check")).toBeInTheDocument();

    const handlerFor = (event: string) => {
      const call = mockSignalRValue.on.mock.calls.find(([name]) => name === event);
      expect(call, `a ${event} handler was registered`).toBeDefined();
      return call![1] as (data: never) => void;
    };

    handlerFor("ConsistencyCheckProgress")({
      message: "Re-checking selected books",
      booksChecked: 1,
      totalBooks: 2,
      issuesFound: 1,
      scope: "selected",
    } as never);

    // The selected run must not drive the full page: no progress state, button stays idle.
    expect(screen.queryByRole("button", { name: "Running Check..." })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Run Consistency Check" })).toBeEnabled();
    expect(
      screen.queryByText("Re-checking selected books (1 issues found)"),
    ).not.toBeInTheDocument();

    handlerFor("ConsistencyCheckComplete")({
      totalBooksChecked: 2,
      totalIssuesFound: 1,
      scope: "selected",
    } as never);

    await waitFor(() => {
      expect(toast.success).not.toHaveBeenCalled();
    });
    expect(screen.queryByText(/Check complete:/)).not.toBeInTheDocument();
  });
});
