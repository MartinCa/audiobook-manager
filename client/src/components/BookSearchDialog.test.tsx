import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor, fireEvent, within } from "@testing-library/react";
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
    await searchWithResults([
      { ...baseResult, source: "Goodreads", duration: undefined, language: undefined },
    ]);

    expect(await screen.findByText("The Boy, the Mole, the Fox and the Horse")).toBeInTheDocument();
    expect(screen.queryByText(/hrs and \d+ mins/)).not.toBeInTheDocument();
    expect(screen.queryByText("English")).not.toBeInTheDocument();
  });

  it("routes a multi-series result through the series-choice dialog and back to the results", async () => {
    await searchWithResults([
      {
        ...baseResult,
        series: [
          { seriesName: "Mistborn", seriesPart: "1" },
          { seriesName: "Mistborn Saga", seriesPart: "2" },
        ],
      },
    ]);

    const applyButtons = await screen.findAllByRole("button", { name: /apply/i });
    fireEvent.click(applyButtons[0]!);

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Select Series")).toBeInTheDocument();
    expect(within(dialog).getByText("Mistborn")).toBeInTheDocument();
    expect(within(dialog).getByText("Mistborn Saga")).toBeInTheDocument();

    // The action stays paged-safe: Back to results returns to the search dialog and results.
    fireEvent.click(within(dialog).getByRole("button", { name: /back to results/i }));

    expect(screen.getByText("Search Online Metadata")).toBeInTheDocument();
    expect(screen.getByText("The Boy, the Mole, the Fox and the Horse")).toBeInTheDocument();
  });

  it("bounds the series-choice dialog to the viewport with a single scroll region", async () => {
    await searchWithResults([
      {
        ...baseResult,
        series: [
          { seriesName: "Mistborn", seriesPart: "1" },
          { seriesName: "Mistborn Saga", seriesPart: "2" },
        ],
      },
    ]);

    const applyButtons = await screen.findAllByRole("button", { name: /apply/i });
    fireEvent.click(applyButtons[0]!);

    // The scrollable-dialog shell (AGENTS.md): width bounded to the viewport, height bounded to
    // 90dvh, outer overflow hidden and a single inner overflow-y-auto body so nothing clips on
    // a narrow phone and scrollbars never nest. The table keeps only horizontal overflow.
    const dialog = await screen.findByRole("dialog");
    expect(dialog.className).toContain("overflow-hidden");
    expect(dialog.className).toContain("flex-col");
    expect(dialog.className).toContain("max-h-[90dvh]");
    expect(dialog.className).toContain("w-[calc(100vw-2rem)]");
    // The dialog shell itself must not carry the base's vertical scroll - that would nest a
    // second scrollbar next to the inner body's.
    expect(dialog.className).not.toContain("overflow-y-auto");
    expect(within(dialog).getByText("Select Series")).toBeInTheDocument();

    const scrollBodies = dialog.querySelectorAll(".overflow-y-auto");
    expect(scrollBodies.length).toBe(1);
    expect(dialog.querySelector(".overflow-x-auto")).not.toBeNull();
  });
});
