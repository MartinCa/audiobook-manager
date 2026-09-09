import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
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
  opts: { seriesItems?: number; standaloneItems?: number; authorId?: number } = {},
): AuthorDetail {
  const authorId = opts.authorId ?? 7;
  return {
    author: { id: authorId, name: "Brandon Sanderson", bookCount: seriesCount + standaloneCount },
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

  return {
    router,
    ...render(
      <ThemeProvider defaultTheme="system" storageKey="theme">
        <SignalRContext.Provider value={mockSignalRValue}>
          <QueryClientProvider client={queryClient}>
            <RouterProvider router={router} />
          </QueryClientProvider>
        </SignalRContext.Provider>
      </ThemeProvider>,
    ),
  };
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

  it("selects standalone books and reflects the page selection in the select-all checkbox", async () => {
    vi.spyOn(browseApi, "getAuthorDetail").mockResolvedValue(makeDetail(0, 2));

    renderWithProviders();

    await screen.findByText(/Standalone 01/);

    const selectAll = screen.getByRole("checkbox", { name: "Select page" });
    expect(selectAll).toHaveAttribute("aria-checked", "false");

    // Picking one of the two standalone books makes the select-all indeterminate.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 01" }));
    expect(selectAll).toHaveAttribute("aria-checked", "mixed");

    // Both picked: fully checked.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 02" }));
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "true",
    );

    // Deselect one: back to indeterminate.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 02" }));
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "mixed",
    );

    // Deselect the last one: the page is cleared entirely.
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 01" }));
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
  });

  // Regression for the review finding: the selection used to be reset only by comparing the
  // prev-id during render, and no test actually changed the route param, so nothing proved the
  // reset fired. Navigating to a second author whose catalogue reuses the same book ids is the
  // exact case that would leak a wrong selection: the same id is now a different book.
  it("clears the selection when navigating to a different author", async () => {
    const getAuthorDetail = vi
      .spyOn(browseApi, "getAuthorDetail")
      .mockImplementation((id) => Promise.resolve(makeDetail(0, 2, { authorId: id })));
    const { router } = renderWithProviders("/library/authors/7");
    await screen.findByText(/Standalone 01/);

    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 01" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Standalone 02" }));
    expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
      "aria-checked",
      "true",
    );

    await router.navigate({ href: "/library/authors/8" });

    await waitFor(() => {
      expect(getAuthorDetail).toHaveBeenLastCalledWith(8, {
        seriesLimit: 50,
        seriesOffset: 0,
        standaloneLimit: 50,
        standaloneOffset: 0,
      });
    });

    // Author 8's standalone books reuse ids 100/101: only a real reset of the selection - not
    // the ids changing out from under it - can leave the new author's rows unchecked.
    await waitFor(() => {
      expect(screen.getByRole("checkbox", { name: "Select page" })).toHaveAttribute(
        "aria-checked",
        "false",
      );
    });
    expect(screen.getByRole("checkbox", { name: "Select Standalone 01" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
    expect(screen.getByRole("checkbox", { name: "Select Standalone 02" })).toHaveAttribute(
      "aria-checked",
      "false",
    );
  });
});
