import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BookSearchDialog } from "./BookSearchDialog";
import { metadataSearchApi } from "@/services/api";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

vi.mock("@/services/api", () => ({
  metadataSearchApi: {
    getServices: vi.fn().mockResolvedValue([
      { name: "Audible", enabled: true },
      { name: "Goodreads", enabled: true },
    ]),
    searchMultiple: vi.fn(),
    getBookDetails: vi.fn(),
    getProxyImageUrl: vi.fn((url: string) => `/proxy?url=${encodeURIComponent(url)}`),
  },
  handleApiError: vi.fn((err: unknown) => ({ message: String(err) })),
}));

const baseResult: MetadataSearchResult = {
  url: "https://www.audible.com/listen/1",
  cleanUrl: "https://www.audible.com/listen/1",
  source: "Audible",
  authors: [{ name: "Charlie Mackesy" }],
  narrators: [{ name: "Charlie Mackesy" }],
  bookName: "The Boy, the Mole, the Fox and the Horse",
  duration: "10 hrs and 23 mins",
  year: 2020,
  language: "English",
  series: [],
  genres: [],
};

function renderDialog() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <BookSearchDialog open onOpenChange={vi.fn()} onSelectResult={vi.fn()} />
    </QueryClientProvider>,
  );
}

async function searchWithResults(results: MetadataSearchResult[]) {
  vi.mocked(metadataSearchApi.searchMultiple).mockResolvedValue({
    results,
    sourceStatuses: [],
  });
  const user = userEvent.setup();
  renderDialog();
  await screen.findByPlaceholderText("Search title, author, or paste URL...");
  await user.type(screen.getByPlaceholderText("Search title, author, or paste URL..."), "mole");
  await user.click(screen.getByRole("button", { name: /search/i }));
  await waitFor(() =>
    expect(metadataSearchApi.searchMultiple).toHaveBeenCalledWith(expect.any(Array), "mole"),
  );
}

describe("BookSearchDialog", () => {
  it("shows duration and language on results when present", async () => {
    await searchWithResults([baseResult]);

    expect(await screen.findByText(/10 hrs and 23 mins/)).toBeInTheDocument();
    expect(screen.getByText("English")).toBeInTheDocument();
    expect(screen.getByText("(2020)")).toBeInTheDocument();
  });

  it("omits duration and language when absent (e.g. Goodreads results)", async () => {
    const { duration: _duration, language: _language, ...noExtras } = baseResult;
    await searchWithResults([{ ...noExtras, source: "Goodreads" }]);

    expect(await screen.findByText("The Boy, the Mole, the Fox and the Horse")).toBeInTheDocument();
    expect(screen.queryByText(/hrs and \d+ mins/)).not.toBeInTheDocument();
    expect(screen.queryByText("English")).not.toBeInTheDocument();
  });
});
