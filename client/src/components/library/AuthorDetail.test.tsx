import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";
import { browseApi } from "@/services/api";
import type { AuthorDetail } from "@/types/AuthorDetail";

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

function makeDetail(
  seriesCount: number,
  standaloneCount: number,
  opts: { seriesItems?: number; standaloneItems?: number } = {},
): AuthorDetail {
  return {
    author: { id: 7, name: "Brandon Sanderson", bookCount: seriesCount + standaloneCount },
    series: {
      count: opts.seriesItems ?? Math.min(seriesCount, 50),
      total: seriesCount,
      items: Array.from({ length: opts.seriesItems ?? Math.min(seriesCount, 50) }, (_, i) => ({
        seriesName: `Series ${String(i + 1).padStart(2, "0")}`,
        bookCount: i + 1,
      })),
    },
    standaloneBooks: {
      count: opts.standaloneItems ?? Math.min(standaloneCount, 50),
      total: standaloneCount,
      items: Array.from(
        { length: opts.standaloneItems ?? Math.min(standaloneCount, 50) },
        (_, i) => ({
          id: 100 + i,
          bookName: `Standalone ${String(i + 1).padStart(2, "0")}`,
          authors: ["Brandon Sanderson"],
          narrators: ["Michael Kramer"],
          genres: ["Fantasy"],
          year: 2000 + i,
          durationInSeconds: 8_000 + i,
        }),
      ),
    },
  };
}

function renderWithProviders(initialEntry = "/library/authors/7") {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [initialEntry] }),
  });

  return render(
    <ThemeProvider defaultTheme="system" storageKey="theme">
      <SignalRContext.Provider value={mockSignalRValue}>
        <QueryClientProvider client={queryClient}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      </SignalRContext.Provider>
    </ThemeProvider>,
  );
}

describe("AuthorDetail", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  // The two sections used to be two separate queries to the same endpoint - each computing
  // (and discarding) the other section's default page. One call carries both section cursors.
  it("fetches the whole author detail in a single call carrying both sections' cursors", async () => {
    const getAuthorDetail = vi
      .spyOn(browseApi, "getAuthorDetail")
      .mockResolvedValue(makeDetail(90, 65));

    renderWithProviders();

    expect(await screen.findByText("Brandon Sanderson")).toBeInTheDocument();
    expect(screen.getByText(/Series \(90\)/)).toBeInTheDocument();
    expect(screen.getByText(/Standalone Audiobooks \(65\)/)).toBeInTheDocument();
    expect(screen.getByText("Series 01")).toBeInTheDocument();
    expect(screen.getByText(/Standalone 01/)).toBeInTheDocument();

    expect(getAuthorDetail).toHaveBeenCalledTimes(1);
    expect(getAuthorDetail).toHaveBeenCalledWith(7, {
      seriesLimit: 50,
      seriesOffset: 0,
      standaloneLimit: 50,
      standaloneOffset: 0,
    });
  });

  it("pages one section through the same combined call, keeping the other section's page", async () => {
    const getAuthorDetail = vi.spyOn(browseApi, "getAuthorDetail");
    getAuthorDetail.mockResolvedValueOnce(makeDetail(90, 65)).mockResolvedValueOnce(
      makeDetail(90, 65, {
        seriesItems: 0,
      }),
    );

    renderWithProviders();

    await screen.findByText("Brandon Sanderson");

    // Both sections are multi-page, so paging the series section means clicking its pager's
    // Next (the series section renders before the standalone section).
    const nextButtons = screen.getAllByRole("button", { name: "Next" });
    expect(nextButtons).toHaveLength(2);
    nextButtons[0]!.click();

    await waitFor(() => {
      expect(getAuthorDetail).toHaveBeenCalledTimes(2);
      expect(getAuthorDetail).toHaveBeenLastCalledWith(7, {
        seriesLimit: 50,
        seriesOffset: 50,
        standaloneLimit: 50,
        standaloneOffset: 0,
      });
    });
  });
});
