import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CleanBookUrls } from "./CleanBookUrls";
import { SignalRContext } from "@/context/SignalRContext";
import { SignalREvents } from "@/constants/signalrEvents";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";
import { notifications } from "@/lib/notifications";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  urlCleanupApi: {
    getDirtyUrlPage: vi.fn(),
    apply: vi.fn(),
    applyAll: vi.fn(),
  },
  operationsApi: {
    getStatus: vi.fn(),
  },
}));

import { urlCleanupApi, operationsApi } from "@/services/api";

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

describe("CleanBookUrls", () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    vi.mocked(urlCleanupApi.getDirtyUrlPage).mockResolvedValue({
      items: [
        {
          audiobookId: 1,
          bookName: "Winter Dark",
          authors: ["Author A"],
          currentUrl: "https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8?qid=123&ref=x",
          cleanedUrl: "https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8",
        },
      ],
      totalCount: 1,
    });

    vi.mocked(urlCleanupApi.apply).mockResolvedValue({ updated: 1 });
    vi.mocked(urlCleanupApi.applyAll).mockResolvedValue(undefined);
    // The operation-status poll on mount resolves as not-running so the resync leaves the
    // page idle rather than reporting a phantom in-flight sweep.
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: false,
      processed: 0,
      total: 0,
    });
  });

  const renderComponent = () =>
    render(
      <SignalRContext.Provider value={mockSignalRValue}>
        <QueryClientProvider client={queryClient}>
          <RouterTestWrapper ui={<CleanBookUrls />} />
        </QueryClientProvider>
      </SignalRContext.Provider>,
    );

  const applyAllProgressHandler = (data: {
    processed: number;
    total: number;
    succeeded: number;
    failed: number;
  }) => {
    const call = [...mockSignalRValue.on.mock.calls]
      .reverse()
      .find(([name]) => name === SignalREvents.UrlCleanupProgress);
    expect(call, "a UrlCleanupProgress handler was registered").toBeDefined();
    (
      call![1] as (data: {
        processed: number;
        total: number;
        succeeded: number;
        failed: number;
      }) => void
    )(data);
  };

  const applyAllCompleteHandler = (data: {
    totalProcessed: number;
    totalSucceeded: number;
    totalFailed: number;
    errored: boolean;
  }) => {
    const call = [...mockSignalRValue.on.mock.calls]
      .reverse()
      .find(([name]) => name === SignalREvents.UrlCleanupComplete);
    expect(call, "a UrlCleanupComplete handler was registered").toBeDefined();
    (
      call![1] as (data: {
        totalProcessed: number;
        totalSucceeded: number;
        totalFailed: number;
        errored: boolean;
      }) => void
    )(data);
  };

  it("shows books with trackable URLs and the cleaned preview", async () => {
    renderComponent();

    expect(await screen.findByText(/Winter Dark/)).toBeInTheDocument();
    expect(
      screen.getByText("https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8?qid=123&ref=x"),
    ).toBeInTheDocument();
    expect(
      screen.getByText("https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8"),
    ).toBeInTheDocument();
  });

  it("shows an empty state when nothing needs cleaning", async () => {
    vi.mocked(urlCleanupApi.getDirtyUrlPage).mockResolvedValue({ items: [], totalCount: 0 });

    renderComponent();

    expect(await screen.findByText("No trackable URLs found")).toBeInTheDocument();
    // No dirty URLs means nothing for "clean all" to do either.
    expect(screen.queryByRole("button", { name: /clean all detected/i })).toBeNull();
  });

  it("applies cleanup for the selected books", async () => {
    renderComponent();

    const applyButton = await screen.findByRole("button", { name: /clean 1 url/i });
    fireEvent.click(applyButton);

    await waitFor(() => {
      expect(urlCleanupApi.apply).toHaveBeenCalledWith([1]);
    });

    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith("Cleaned 1 book URL");
    });
  });

  it("excludes a book from apply when its checkbox is unchecked", async () => {
    renderComponent();

    const checkbox = await screen.findByRole("checkbox");
    fireEvent.click(checkbox);

    const applyButton = await screen.findByRole("button", { name: /clean 0 urls/i });
    expect(applyButton).toBeDisabled();
  });

  it("cleans every detected URL via the fire-and-forget apply-all endpoint", async () => {
    renderComponent();
    await screen.findByText(/Winter Dark/);

    fireEvent.click(screen.getByRole("button", { name: /clean all detected urls/i }));

    await waitFor(() => {
      expect(urlCleanupApi.applyAll).toHaveBeenCalledTimes(1);
    });
    expect(notifications.success).toHaveBeenCalledWith(
      "Cleaning all 1 detected URLs in the background",
    );
  });

  it("shows live progress while the apply-all sweep runs and re-reads the list on completion", async () => {
    renderComponent();
    await screen.findByText(/Winter Dark/);

    applyAllProgressHandler({ processed: 1, total: 2, succeeded: 1, failed: 0 });
    await screen.findByText(/Cleaning all detected URLs \(1 cleaned, 0 failed\)/);
    expect(screen.getByRole("button", { name: /clean all detected urls/i })).toBeDisabled();

    applyAllCompleteHandler({
      totalProcessed: 2,
      totalSucceeded: 2,
      totalFailed: 0,
      errored: false,
    });
    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith("Cleaned 2 book URLs");
    });
    // The bar is gone and the list is re-read so the freshly-cleaned set renders.
    await waitFor(() => {
      expect(screen.queryByText(/Cleaning all detected URLs/)).toBeNull();
    });
    expect(urlCleanupApi.getDirtyUrlPage).toHaveBeenCalledTimes(2);
  });

  it("warns instead of celebrating when the apply-all sweep had failures", async () => {
    renderComponent();
    await screen.findByText(/Winter Dark/);

    applyAllProgressHandler({ processed: 2, total: 2, succeeded: 1, failed: 1 });
    applyAllCompleteHandler({
      totalProcessed: 2,
      totalSucceeded: 1,
      totalFailed: 1,
      errored: false,
    });

    await waitFor(() => {
      expect(notifications.warning).toHaveBeenCalledWith("Cleaned 1 book URL (1 failed)");
    });
    // A partial failure must not be dressed up as a success.
    expect(notifications.success).not.toHaveBeenCalledWith("Cleaned 1 book URL (1 failed)");
    // The list is still re-read: the books that DID get cleaned are gone from it.
    expect(urlCleanupApi.getDirtyUrlPage).toHaveBeenCalledTimes(2);
  });

  it("reports an errored apply-all sweep as a failure, not a zero-count success", async () => {
    renderComponent();
    await screen.findByText(/Winter Dark/);

    // BackgroundOperationRunner's error path: every count is zero, which would otherwise read as
    // "the sweep had nothing left to do" - errored is what tells the two apart.
    applyAllCompleteHandler({
      totalProcessed: 0,
      totalSucceeded: 0,
      totalFailed: 0,
      errored: true,
    });

    await waitFor(() => {
      expect(notifications.error).toHaveBeenCalledWith("URL cleanup failed");
    });
    expect(notifications.success).not.toHaveBeenCalled();
    expect(notifications.warning).not.toHaveBeenCalled();
  });

  it("recovers an in-flight apply-all from the operation status on mount", async () => {
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: true,
      processed: 3,
      total: 10,
    });

    renderComponent();

    await waitFor(() => {
      expect(screen.getByText(/Cleaning all detected URLs/)).toBeInTheDocument();
    });
    // The recovered progress carries the registry's processed/total until a live event arrives.
    expect(screen.getByText(/3 \/ 10/)).toBeInTheDocument();
  });

  it("shows a pager and fetches the next page of dirty URLs", async () => {
    vi.mocked(urlCleanupApi.getDirtyUrlPage)
      .mockResolvedValueOnce({
        items: [
          {
            audiobookId: 1,
            bookName: "First Book",
            authors: ["Author A"],
            currentUrl: "https://a.example/x?q=1",
            cleanedUrl: "https://a.example/x",
          },
        ],
        totalCount: 125,
      })
      .mockResolvedValueOnce({
        items: [
          {
            audiobookId: 51,
            bookName: "Second Book",
            authors: ["Author B"],
            currentUrl: "https://b.example/x?q=2",
            cleanedUrl: "https://b.example/x",
          },
        ],
        totalCount: 125,
      });

    renderComponent();

    expect(await screen.findByText(/First Book/)).toBeInTheDocument();
    // The header count is the whole matching set (125), not the loaded page (which has 1 item).
    expect(screen.getByText("Books with Trackable URLs (125)")).toBeInTheDocument();
    expect(screen.getByText(/Showing 1–50 of 125/)).toBeInTheDocument();

    const nextButton = screen.getByRole("button", { name: /next/i });
    fireEvent.click(nextButton);

    expect(await screen.findByText(/Second Book/)).toBeInTheDocument();
    expect(urlCleanupApi.getDirtyUrlPage).toHaveBeenLastCalledWith(1, 50);
    expect(screen.getByText(/Showing 51–100 of 125/)).toBeInTheDocument();
  });

  // Regression (PR #1326 review): the selection is page-scoped. Un-ticking a box on page 0 and
  // navigating must reset back to "everything on the visible page selected" - otherwise the apply
  // button winds up enabled with a count of books the user can't see from page 1.
  it("resets the selection when navigating pages", async () => {
    vi.mocked(urlCleanupApi.getDirtyUrlPage)
      .mockResolvedValueOnce({
        items: [
          {
            audiobookId: 1,
            bookName: "First Book",
            authors: ["Author A"],
            currentUrl: "https://a.example/x?q=1",
            cleanedUrl: "https://a.example/x",
          },
        ],
        // Over PAGE_SIZE so the pager (and the Next button) renders even though the mock only
        // ever yields one item per page.
        totalCount: 51,
      })
      .mockResolvedValueOnce({
        items: [
          {
            audiobookId: 2,
            bookName: "Second Book",
            authors: ["Author B"],
            currentUrl: "https://b.example/x?q=2",
            cleanedUrl: "https://b.example/x",
          },
        ],
        totalCount: 51,
      });

    renderComponent();

    const firstCheckbox = await screen.findByRole("checkbox");
    fireEvent.click(firstCheckbox);
    expect(await screen.findByRole("button", { name: /clean 0 urls/i })).toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: /next/i }));

    // Navigating away resets the selection to the default of the new page, so the button is
    // enabled again for the book now in front of the user - not left counting the page-0 set.
    await screen.findByText(/Second Book/);
    expect(screen.getByRole("button", { name: /clean 1 url/i })).toBeEnabled();
  });

  it("shows skeleton loading rows while the dirty URL list loads", async () => {
    vi.mocked(urlCleanupApi.getDirtyUrlPage).mockImplementation(() => new Promise(() => {}));

    const { container } = renderComponent();

    expect(
      await screen.findByRole("status", { name: "Scanning saved URLs..." }),
    ).toBeInTheDocument();
    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0);
  });
});
