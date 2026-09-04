import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CleanBookUrls } from "./CleanBookUrls";
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

vi.mock("@/services/api", () => ({
  urlCleanupApi: {
    getDirtyUrlPage: vi.fn(),
    apply: vi.fn(),
  },
}));

import { urlCleanupApi } from "@/services/api";

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
  });

  const renderComponent = () =>
    render(
      <QueryClientProvider client={queryClient}>
        <RouterTestWrapper ui={<CleanBookUrls />} />
      </QueryClientProvider>,
    );

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
  });

  it("applies cleanup for the selected books", async () => {
    renderComponent();

    const applyButton = await screen.findByRole("button", { name: /clean 1 url/i });
    fireEvent.click(applyButton);

    await waitFor(() => {
      expect(urlCleanupApi.apply).toHaveBeenCalledWith([1]);
    });

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Cleaned 1 book URL");
    });
  });

  it("excludes a book from apply when its checkbox is unchecked", async () => {
    renderComponent();

    const checkbox = await screen.findByRole("checkbox");
    fireEvent.click(checkbox);

    const applyButton = await screen.findByRole("button", { name: /clean 0 urls/i });
    expect(applyButton).toBeDisabled();
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
});
