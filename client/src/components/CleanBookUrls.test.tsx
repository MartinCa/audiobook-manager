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
    getDirtyUrls: vi.fn(),
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

    vi.mocked(urlCleanupApi.getDirtyUrls).mockResolvedValue([
      {
        audiobookId: 1,
        bookName: "Winter Dark",
        authors: ["Author A"],
        currentUrl: "https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8?qid=123&ref=x",
        cleanedUrl: "https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8",
      },
    ]);

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
    vi.mocked(urlCleanupApi.getDirtyUrls).mockResolvedValue([]);

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
});
