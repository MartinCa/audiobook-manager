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

  // Regression: on a narrow phone the result card's action row was `shrink-0` and its Apply
  // button `w-full` — the button could not shrink below its 100%-width basis alongside the
  // "View Source" link, so it extended past the overflow-hidden dialog edge and was clipped
  // (measured right edge 428px against a 396px dialog edge at a 412px viewport).
  it("lets the result card's action row shrink inside the dialog on mobile", async () => {
    await searchWithResults([baseResult]);

    const apply = await screen.findByRole("button", { name: /apply/i });
    // The Apply button must be allowed to shrink to the space left of "View Source" rather
    // than demand its full-width basis; flex-1 with a min-w-0 floor does exactly that.
    expect(apply.className).toContain("flex-1");
    expect(apply.className).toContain("min-w-0");
    expect(apply.className).not.toContain("w-full");

    // And the row wrapping it must not be shrink-0, so the row itself can give up width.
    const row = apply.closest("div")!;
    expect(row.className).not.toContain("shrink-0");
  });

  // Regression test: deselecting every source used to silently re-expand `activeSources` back to
  // every enabled source (an `activeSources.length > 0 ? activeSources : allEnabled` fallback
  // duplicated in this component, on top of the one useSelectedSearchSources itself already
  // provides for "no preference yet"), making the Search button's own empty-selection guard
  // unreachable. Search must disable instead, matching BulkOnlineMatchSearchDialog's identical
  // fix for the same shared hook.
  it("disables Search, rather than silently re-selecting every source, once the user toggles every source off", async () => {
    renderDialog();
    await screen.findByText("Audible");
    // A query alone must not enable Search - it also needs a non-empty source selection, so
    // typing one here isolates the assertion to that guard rather than the query's own.
    fireEvent.change(screen.getByPlaceholderText("Search title, author, or paste URL..."), {
      target: { value: "mole" },
    });

    fireEvent.click(screen.getByText("Audible"));
    fireEvent.click(screen.getByText("Goodreads"));

    expect(screen.getByRole("button", { name: /^search$/i })).toBeDisabled();
  });

  // Regression: BookSearchDialog's placeholder ("...or paste URL...") and AGENTS.md both
  // document pasting a book URL (e.g. https://hardcover.app/books/1984) as going straight to
  // metadataSearchApi.getBookDetails() - skipping source selection and the multi-source text
  // search entirely, so the backend can check which registered scraper's SupportsUrl() matches
  // it. That branch was missing: submitting a URL used to fall through to searchMultiple like any
  // other text query, which cannot resolve a URL to a book at all.
  describe("pasting a book URL", () => {
    const bookUrl = "https://hardcover.app/books/1984";

    it("calls getBookDetails directly instead of the multi-source search, even with no source selected", async () => {
      vi.mocked(metadataSearchApi.getBookDetails).mockResolvedValue(baseResult);
      const onSelectResult = vi.fn();
      const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const user = userEvent.setup();
      render(
        <QueryClientProvider client={queryClient}>
          <BookSearchDialog open onOpenChange={vi.fn()} onSelectResult={onSelectResult} />
        </QueryClientProvider>,
      );

      await screen.findByText("Audible");
      // Deselecting every source would disable Search for a plain text query (see the test
      // above) - a URL must not need any source selected at all.
      fireEvent.click(screen.getByText("Audible"));
      fireEvent.click(screen.getByText("Goodreads"));

      await user.type(
        screen.getByPlaceholderText("Search title, author, or paste URL..."),
        bookUrl,
      );
      expect(screen.getByRole("button", { name: /^search$/i })).not.toBeDisabled();
      await user.click(screen.getByRole("button", { name: /^search$/i }));

      await waitFor(() => expect(metadataSearchApi.getBookDetails).toHaveBeenCalledWith(bookUrl));
      expect(metadataSearchApi.searchMultiple).not.toHaveBeenCalled();
      await waitFor(() => expect(onSelectResult).toHaveBeenCalledWith(baseResult));
    });

    it("shows the backend's error when no configured source supports the URL", async () => {
      vi.mocked(metadataSearchApi.getBookDetails).mockRejectedValue(
        new Error(
          "No configured metadata source supports the URL 'https://hardcover.app/books/1984'.",
        ),
      );
      const user = userEvent.setup();
      renderDialog();

      await screen.findByText("Audible");
      await user.type(
        screen.getByPlaceholderText("Search title, author, or paste URL..."),
        bookUrl,
      );
      await user.click(screen.getByRole("button", { name: /^search$/i }));

      expect(
        await screen.findByText(/no configured metadata source supports the url/i),
      ).toBeInTheDocument();
      expect(metadataSearchApi.searchMultiple).not.toHaveBeenCalled();
    });
  });
});
