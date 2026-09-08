import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { routeTree } from "@/routeTree.gen";
import { SignalRContext } from "@/context/SignalRContext";
import { ThemeProvider } from "@/components/theme-provider";
import { browseApi } from "@/services/api";

vi.mock("@/services/api", () => ({
  browseApi: {
    getAuthorPage: vi.fn(),
  },
}));

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false } },
});

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

function renderWithProviders(initialEntry = "/library/authors") {
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

function makeAuthor(id: number) {
  return { id, name: `Author ${String(id).padStart(2, "0")}`, bookCount: id };
}

describe("AuthorsList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders authors from the server page with an overall count", async () => {
    vi.mocked(browseApi.getAuthorPage).mockResolvedValue({
      count: 1,
      total: 3,
      items: [makeAuthor(1)],
    });

    renderWithProviders();

    expect(await screen.findByText("Author 01")).toBeInTheDocument();
    expect(screen.getByText(/Authors \(3\)/)).toBeInTheDocument();
  });

  it("pages server-side via limit/offset", async () => {
    const call = vi.mocked(browseApi.getAuthorPage);
    call
      .mockResolvedValueOnce({
        count: 1,
        total: 65,
        items: [makeAuthor(1)],
      })
      .mockResolvedValueOnce({
        count: 1,
        total: 65,
        items: [makeAuthor(51)],
      });

    renderWithProviders();

    const nextButton = await screen.findByRole("button", { name: "Next" });
    nextButton.click();

    await waitFor(() => {
      // 50-row pages; the next one begins at offset 50.
      expect(browseApi.getAuthorPage).toHaveBeenCalledWith(50, 50, "");
    });

    expect(await screen.findByText("Author 51")).toBeInTheDocument();
  });

  it("sends the debounced filter to the server as the search term", async () => {
    vi.mocked(browseApi.getAuthorPage).mockResolvedValue({ count: 0, total: 0, items: [] });

    renderWithProviders();

    const searchInput = await screen.findByPlaceholderText("Filter authors...");
    fireEvent.change(searchInput, { target: { value: "rene" } });

    await waitFor(() => {
      expect(browseApi.getAuthorPage).toHaveBeenCalledWith(50, 0, "rene");
    });
  });
});
