import { describe, it, expect, vi, beforeEach } from "vitest";
import { useState } from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SignalRContext } from "@/context/SignalRContext";
import { BookBulkActionBar } from "./BookBulkActionBar";
import { useBookSelection, type SelectedBookInfo } from "@/hooks/useBookSelection";
import type * as SonnerModule from "sonner";
import { toast } from "sonner";
import type * as ApiModule from "@/services/api";
import { consistencyApi, metadataRefreshApi, operationsApi } from "@/services/api";

vi.mock("sonner", async (importOriginal) => {
  const actual = await importOriginal<typeof SonnerModule>();
  return {
    ...actual,
    toast: {
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
      warning: vi.fn(),
    },
  };
});

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    bulkEditApi: {
      preview: vi.fn(),
      apply: vi.fn(),
    },
    operationsApi: {
      getStatus: vi.fn().mockResolvedValue({ isRunning: false, processed: 0, total: 0 }),
    },
    metadataRefreshApi: {
      ...actual.metadataRefreshApi,
      refreshSelected: vi.fn().mockResolvedValue(undefined),
    },
    consistencyApi: {
      ...actual.consistencyApi,
      checkSelected: vi.fn().mockResolvedValue(undefined),
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

// Real useBookSelection, seeded from props the same way a view's row checkboxes would be, so
// "Clear selection" and the bulk-edit-complete clear actually observable-empty the selection.
function Harness({ initialBooks }: { initialBooks: SelectedBookInfo[] }) {
  const selection = useBookSelection();
  // Seed once per selection while rendering — the same guarded adjust-during-render pattern
  // AuthorDetail/SeriesDetail use to reset state when an entity changes.
  const [seededKey, setSeededKey] = useState<string | null>(null);
  const key = initialBooks.map((b) => `${b.id}:\u0000${b.title}`).join("|");
  if (seededKey !== key) {
    setSeededKey(key);
    for (const bookInfo of initialBooks) {
      selection.toggle({
        id: bookInfo.id,
        bookName: bookInfo.title,
        authors: bookInfo.authors,
        narrators: [],
        genres: [],
      });
    }
  }

  return <BookBulkActionBar selection={selection} />;
}

function renderBar(initialBooks: SelectedBookInfo[] = []) {
  queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

  render(
    <SignalRContext.Provider value={mockSignalRValue}>
      <QueryClientProvider client={queryClient}>
        <Harness initialBooks={initialBooks} />
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );

  return { invalidateSpy };
}

const books: SelectedBookInfo[] = [
  { id: 1, title: "The Way of Kings", authors: ["Brandon Sanderson"] },
  { id: 7, title: "Words of Radiance", authors: ["Brandon Sanderson"] },
];

function handlerFor(event: string) {
  const call = mockSignalRValue.on.mock.calls.find(([name]) => name === event);
  expect(call, `a ${event} handler was registered`).toBeDefined();
  return call![1] as (data: never) => void;
}

describe("BookBulkActionBar", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders nothing when nothing is selected and no operation is running", () => {
    renderBar([]);

    expect(screen.queryByText(/selected/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Refresh Metadata" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Clear selection" })).not.toBeInTheDocument();
  });

  it("shows the selection count and a preview of the selected titles", async () => {
    renderBar(books);

    expect(await screen.findByText("2 selected")).toBeInTheDocument();
    // The titles preview must not claim titles beyond what is shown: with two of two previewed,
    // no "and N more" suffix.
    expect(screen.getByText("The Way of Kings, Words of Radiance")).toBeInTheDocument();
  });

  it("shows '…and N more' when the selection exceeds the title preview", async () => {
    const many = [
      { id: 1, title: "One", authors: [] },
      { id: 2, title: "Two", authors: [] },
      { id: 3, title: "Three", authors: [] },
      { id: 4, title: "Four", authors: [] },
    ];
    renderBar(many);

    expect(await screen.findByText("4 selected")).toBeInTheDocument();
    expect(screen.getByText("One, Two, Three… and 1 more")).toBeInTheDocument();
  });

  it("calls refreshSelected with exactly the selected ids and toasts the start", async () => {
    renderBar(books);
    await screen.findByText("2 selected");

    fireEvent.click(screen.getByRole("button", { name: "Refresh Metadata" }));

    await waitFor(() => {
      expect(metadataRefreshApi.refreshSelected).toHaveBeenCalledWith([1, 7]);
    });
    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Refreshing metadata for 2 books…");
    });
  });

  it("calls checkSelected with exactly the selected ids and toasts the start", async () => {
    renderBar(books);
    await screen.findByText("2 selected");

    fireEvent.click(screen.getByRole("button", { name: "Check Consistency" }));

    await waitFor(() => {
      expect(consistencyApi.checkSelected).toHaveBeenCalledWith([1, 7]);
    });
    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Consistency check started for 2 books");
    });
  });

  it("toasts the error when the refresh start is refused", async () => {
    vi.mocked(metadataRefreshApi.refreshSelected).mockRejectedValueOnce(
      new Error("An operation is already in progress."),
    );
    renderBar(books);
    await screen.findByText("2 selected");

    fireEvent.click(screen.getByRole("button", { name: "Refresh Metadata" }));

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith("An operation is already in progress.");
    });
  });

  it("clears the selection through the Clear selection button", async () => {
    renderBar(books);
    await screen.findByText("2 selected");

    fireEvent.click(screen.getByRole("button", { name: "Clear selection" }));

    await waitFor(() => {
      expect(screen.queryByText("2 selected")).not.toBeInTheDocument();
    });
    expect(screen.queryByRole("button", { name: "Refresh Metadata" })).not.toBeInTheDocument();
  });

  it("reports a running operation from the status registry and disables every action", async () => {
    const statuses: Record<string, { isRunning: boolean; processed: number; total: number }> = {
      "bulk-edit": { isRunning: true, processed: 1, total: 3 },
    };
    vi.mocked(operationsApi.getStatus).mockImplementation((key) =>
      Promise.resolve(statuses[key] ?? { isRunning: false, processed: 0, total: 0 }),
    );

    renderBar();
    // Idle and empty, but the running operation keeps the bar mounted with progress.
    expect(await screen.findByText("Bulk editing books...")).toBeInTheDocument();

    const actions = ["Refresh Metadata", "Check Consistency", "Edit Metadata", "Clear selection"];
    for (const name of actions) {
      expect(screen.getByRole("button", { name })).toBeDisabled();
    }
  });

  it("keeps progress visible after a disconnected browser misses the resume and re-subscribes", async () => {
    const statuses: Record<string, { isRunning: boolean; processed: number; total: number }> = {
      "consistency-check-selected": { isRunning: true, processed: 4, total: 10 },
    };
    vi.mocked(operationsApi.getStatus).mockImplementation((key) =>
      Promise.resolve(statuses[key] ?? { isRunning: false, processed: 0, total: 0 }),
    );

    renderBar();
    expect(await screen.findByText(/Resuming check/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Edit Metadata" })).toBeDisabled();
  });

  it("shows bulk-edit progress from SignalR and on completion toasts, invalidates views and clears the selection", async () => {
    const { invalidateSpy } = renderBar(books);
    await screen.findByText("2 selected");

    handlerFor("BulkEditProgress")({
      processed: 1,
      total: 2,
      succeeded: 1,
      failed: 0,
    } as never);
    expect(await screen.findByText("Bulk editing books...")).toBeInTheDocument();

    handlerFor("BulkEditComplete")({ processed: 2, succeeded: 2, failed: 0 } as never);

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Bulk edit complete: 2 updated");
    });
    expect(screen.queryByText("Bulk editing books...")).not.toBeInTheDocument();

    for (const queryKey of [
      ["books"],
      ["author"],
      ["seriesDetail"],
      ["metadataRefresh"],
      ["similarValueNames"],
    ]) {
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey });
    }

    // Edits were applied: the selection that described the books is dropped.
    await waitFor(() => {
      expect(screen.queryByText("2 selected")).not.toBeInTheDocument();
    });
  });

  it("warns when a bulk edit had failures and still clears the selection", async () => {
    renderBar(books);
    await screen.findByText("2 selected");

    handlerFor("BulkEditComplete")({ processed: 2, succeeded: 1, failed: 1 } as never);

    await waitFor(() => {
      expect(toast.warning).toHaveBeenCalledWith("Bulk edit complete: 1 updated, 1 failed");
    });
    await waitFor(() => {
      expect(screen.queryByText("2 selected")).not.toBeInTheDocument();
    });
  });

  it("shows metadata-refresh progress and on completion toasts and invalidates without clearing the selection", async () => {
    const { invalidateSpy } = renderBar(books);
    await screen.findByText("2 selected");

    handlerFor("MetadataRefreshProgress")({
      processed: 1,
      total: 2,
      succeeded: 1,
      failed: 0,
    } as never);
    expect(await screen.findByText("Refreshing metadata...")).toBeInTheDocument();

    handlerFor("MetadataRefreshComplete")({
      totalProcessed: 2,
      total: 2,
      totalSucceeded: 2,
      totalFailed: 0,
    } as never);

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith(
        "Metadata refresh complete: 2 refreshed, 0 failed",
      );
    });
    expect(screen.queryByText("Refreshing metadata...")).not.toBeInTheDocument();
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["books"] });
    // A refresh does not change the books' identities: the selection survives.
    expect(screen.getByText("2 selected")).toBeInTheDocument();
    expect(invalidateSpy).not.toHaveBeenCalledWith({ queryKey: ["similarValueNames"] });
  });

  it("warns when a metadata refresh stops early", async () => {
    renderBar(books);
    await screen.findByText("2 selected");

    handlerFor("MetadataRefreshComplete")({
      totalProcessed: 1,
      total: 2,
      totalSucceeded: 1,
      totalFailed: 1,
      stopReason: "Hardcover daily limit reached",
    } as never);

    await waitFor(() => {
      expect(toast.warning).toHaveBeenCalledWith(
        "Hardcover daily limit reached. 1 succeeded, 1 failed.",
      );
    });
  });

  it("shows selected consistency-check progress and completes with a toast", async () => {
    const { invalidateSpy } = renderBar(books);
    await screen.findByText("2 selected");

    handlerFor("ConsistencyCheckProgress")({
      message: "Checking books",
      booksChecked: 1,
      totalBooks: 2,
      issuesFound: 3,
    } as never);
    expect(await screen.findByText("Checking books (3 issues found)")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Edit Metadata" })).toBeDisabled();

    handlerFor("ConsistencyCheckComplete")({
      totalBooksChecked: 2,
      totalIssuesFound: 3,
    } as never);

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Check complete: 2 books checked, 3 issues found");
    });
    expect(screen.queryByText("Checking books (3 issues found)")).not.toBeInTheDocument();
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["seriesDetail"] });
    expect(screen.getByText("2 selected")).toBeInTheDocument();
  });
});
