import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { TagPreviewDialog } from "./TagPreviewDialog";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false } },
});

function renderWithQuery(ui: React.ReactElement) {
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("TagPreviewDialog", () => {
  const currentInput: OrganizeAudiobookInput = {
    bookName: "Old Title",
    authors: "Old Author",
    year: 2020,
  };

  const searchResult: MetadataSearchResult = {
    url: "https://audible.com/pd/123",
    cleanUrl: "https://audible.com/pd/123",
    source: "Audible",
    bookName: "New Title",
    authors: [{ name: "New Author" }],
    narrators: [],
    series: [],
    genres: [],
    year: 2021,
  };

  it("displays current and new scraped values", () => {
    renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={currentInput}
        searchResult={searchResult}
        onApply={() => {}}
      />,
    );

    expect(screen.getByText("Old Title")).toBeInTheDocument();
    expect(screen.getByText("New Title")).toBeInTheDocument();
    expect(screen.getByText("Old Author")).toBeInTheDocument();
    expect(screen.getByText("New Author")).toBeInTheDocument();
  });

  it("calls onApply with all keys and saveImmediately=true when Apply All is clicked", () => {
    const onApply = vi.fn();

    renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={currentInput}
        searchResult={searchResult}
        onApply={onApply}
      />,
    );

    // Without showAutoSaveToggle (the pending-refresh snapshot flow), the label is unchanged
    // ("Apply All"), but onApply still reports saveImmediately=true - that flow always saves.
    const applyAllBtn = screen.getByText("Apply All");
    fireEvent.click(applyAllBtn);

    expect(onApply).toHaveBeenCalledTimes(1);
    const [, appliedKeys, saveImmediately] = onApply.mock.calls[0] as [
      MetadataSearchResult,
      Set<string>,
      boolean,
    ];
    expect(appliedKeys.has("bookName")).toBe(true);
    expect(appliedKeys.has("authors")).toBe(true);
    expect(saveImmediately).toBe(true);
  });

  it("defaults the auto-save toggle to off and lets it opt out of saving on each fresh flow", () => {
    const onApply = vi.fn();
    const { unmount } = renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={currentInput}
        searchResult={searchResult}
        onApply={onApply}
        showAutoSaveToggle
      />,
    );

    // Default: toggle off, Apply saves immediately.
    expect(screen.getByText("Apply & Save All")).toBeInTheDocument();

    const toggle = screen.getByRole("checkbox", { name: "Don't save automatically" });
    fireEvent.click(toggle);
    expect(screen.getByText("Apply All")).toBeInTheDocument();

    fireEvent.click(screen.getByText("Apply All"));
    expect(onApply).toHaveBeenCalledTimes(1);
    const [, , saveImmediately] = onApply.mock.calls[0] as [
      MetadataSearchResult,
      Set<string>,
      boolean,
    ];
    expect(saveImmediately).toBe(false);
    unmount();

    // A fresh mount of the dialog (a new flow) must not remember the previous flow's toggle
    // state — it starts back at the default (off = save immediately).
    renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={currentInput}
        searchResult={searchResult}
        onApply={() => {}}
        showAutoSaveToggle
      />,
    );
    expect(screen.getByText("Apply & Save All")).toBeInTheDocument();
  });

  // Regression test for the wasOpen fix specifically: the identity-based reset alone would miss
  // this, since the dialog stays mounted (BookEditForm never unmounts it between searches) and a
  // future cache hit could hand back the exact same searchResult reference for a second flow.
  it("resets the auto-save toggle when the dialog closes and reopens with the same result object", () => {
    const onApply = vi.fn();
    const { rerender } = renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={currentInput}
        searchResult={searchResult}
        onApply={onApply}
        showAutoSaveToggle
      />,
    );

    fireEvent.click(screen.getByRole("checkbox", { name: "Don't save automatically" }));
    expect(screen.getByText("Apply All")).toBeInTheDocument();

    // Close the dialog (component stays mounted - only `open` flips) and reopen it with the
    // exact same searchResult reference, as a real second flow reusing a cached search result
    // would.
    rerender(
      <QueryClientProvider client={queryClient}>
        <TagPreviewDialog
          open={false}
          onOpenChange={() => {}}
          currentInput={currentInput}
          searchResult={searchResult}
          onApply={onApply}
          showAutoSaveToggle
        />
      </QueryClientProvider>,
    );
    rerender(
      <QueryClientProvider client={queryClient}>
        <TagPreviewDialog
          open={true}
          onOpenChange={() => {}}
          currentInput={currentInput}
          searchResult={searchResult}
          onApply={onApply}
          showAutoSaveToggle
        />
      </QueryClientProvider>,
    );

    expect(screen.getByText("Apply & Save All")).toBeInTheDocument();
  });

  it("displays normalized language name in preview diff", () => {
    renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={{ ...currentInput, language: "da" }}
        searchResult={{ ...searchResult, language: "English" }}
        onApply={() => {}}
      />,
    );

    expect(screen.getByText("Danish")).toBeInTheDocument();
    expect(screen.getByText("English")).toBeInTheDocument();
  });

  // Regression test for the Year row fix: the source reporting no year must not be advertised as
  // a change the apply will never make, so Year must not land in the diff-derived selection that
  // "Apply Selected" hands to onApply. Uses Apply Selected (the set built from the diff), not
  // Apply All - Apply All deliberately returns every field key regardless of whether it changed.
  it("does not select Year when the source reports no year", () => {
    const onApply = vi.fn();

    renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={{ ...currentInput, year: 2020 }}
        searchResult={{ ...searchResult, year: undefined }}
        onApply={onApply}
      />,
    );

    const applySelectedBtn = screen.getByRole("button", { name: /selected/i });
    fireEvent.click(applySelectedBtn);

    expect(onApply).toHaveBeenCalledTimes(1);
    const [, appliedKeys] = onApply.mock.calls[0] as [MetadataSearchResult, Set<string>];
    expect(appliedKeys.has("year")).toBe(false);
  });

  it("selects Year when the source reports a different defined year", () => {
    const onApply = vi.fn();

    renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={{ ...currentInput, year: 2020 }}
        searchResult={{ ...searchResult, year: 2021 }}
        onApply={onApply}
      />,
    );

    const applySelectedBtn = screen.getByRole("button", { name: /selected/i });
    fireEvent.click(applySelectedBtn);

    expect(onApply).toHaveBeenCalledTimes(1);
    const [, appliedKeys] = onApply.mock.calls[0] as [MetadataSearchResult, Set<string>];
    expect(appliedKeys.has("year")).toBe(true);
  });

  // Regression for the title-splitting bug: with the split toggle left at its default (off), a
  // scraped title is never mangled - including a bare colon that isn't a "Title: Subtitle" pair
  // (e.g. "4:50 from Paddington", where the colon is a train time).
  it("does not split the scraped title by default, even when it contains a colon", () => {
    const onApply = vi.fn();

    renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={currentInput}
        searchResult={{ ...searchResult, bookName: "4:50 from Paddington" }}
        onApply={onApply}
      />,
    );

    expect(screen.getByText("4:50 from Paddington")).toBeInTheDocument();

    fireEvent.click(screen.getByText("Apply All"));

    expect(onApply).toHaveBeenCalledTimes(1);
    const [appliedResult] = onApply.mock.calls[0] as [MetadataSearchResult, Set<string>, boolean];
    expect(appliedResult.bookName).toBe("4:50 from Paddington");
    expect(appliedResult.subtitle).toBeUndefined();
  });

  // Checking the toggle splits a genuine "Title: Subtitle" title, and the split values are what
  // get passed to onApply (BookEditForm's consumer reads bookName/subtitle straight off the
  // result it is handed).
  it("splits the scraped title into book name and subtitle when the toggle is checked", () => {
    const onApply = vi.fn();

    renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={currentInput}
        searchResult={{ ...searchResult, bookName: "The Hobbit: There and Back Again" }}
        onApply={onApply}
      />,
    );

    const toggle = screen.getByRole("checkbox", {
      name: "Split title into book name and subtitle at first colon",
    });
    fireEvent.click(toggle);

    fireEvent.click(screen.getByText("Apply All"));

    expect(onApply).toHaveBeenCalledTimes(1);
    const [appliedResult] = onApply.mock.calls[0] as [MetadataSearchResult, Set<string>, boolean];
    expect(appliedResult.bookName).toBe("The Hobbit");
    expect(appliedResult.subtitle).toBe("There and Back Again");
  });

  // Regression test: checking the toggle AFTER the dialog has already opened makes "subtitle"
  // newly changed in the diff table, but `selected` was computed before the flip and would keep
  // "bookName" only unless it's re-synced. Apply Selected must pick up the newly-split subtitle,
  // not just Apply All (which always sends every currently-changed key regardless of `selected`).
  it("re-syncs selected fields when the split toggle is flipped mid-session, so Apply Selected keeps the recovered subtitle", () => {
    const onApply = vi.fn();

    renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={currentInput}
        searchResult={{ ...searchResult, bookName: "The Hobbit: There and Back Again" }}
        onApply={onApply}
      />,
    );

    // Toggle off initially: "subtitle" is unchanged, so it starts out of `selected`.
    const toggle = screen.getByRole("checkbox", {
      name: "Split title into book name and subtitle at first colon",
    });
    fireEvent.click(toggle);

    fireEvent.click(screen.getByRole("button", { name: /selected/i }));

    expect(onApply).toHaveBeenCalledTimes(1);
    const [appliedResult, appliedKeys] = onApply.mock.calls[0] as [
      MetadataSearchResult,
      Set<string>,
      boolean,
    ];
    expect(appliedKeys.has("subtitle")).toBe(true);
    expect(appliedResult.bookName).toBe("The Hobbit");
    expect(appliedResult.subtitle).toBe("There and Back Again");
  });

  // Regression test: with the toggle on but "Book Name" manually deselected, the recovered tail
  // must not be duplicated into subtitle while the untouched book name still carries the full raw
  // title (BookEditForm leaves bookName alone when it's not in the selected keys). Only the
  // pre-split, blank subtitle should reach onApply - the split has nowhere to have "moved" the
  // text from when book name itself is not being applied.
  it("does not duplicate the recovered subtitle into the result when book name is deselected", () => {
    const onApply = vi.fn();

    renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={currentInput}
        searchResult={{ ...searchResult, bookName: "The Hobbit: There and Back Again" }}
        onApply={onApply}
      />,
    );

    const splitToggle = screen.getByRole("checkbox", {
      name: "Split title into book name and subtitle at first colon",
    });
    fireEvent.click(splitToggle);

    const bookNameRow = screen.getByText("Book Name").closest("tr");
    expect(bookNameRow).not.toBeNull();
    fireEvent.click(within(bookNameRow!).getByRole("checkbox"));

    fireEvent.click(screen.getByRole("button", { name: /selected/i }));

    expect(onApply).toHaveBeenCalledTimes(1);
    const [appliedResult, appliedKeys] = onApply.mock.calls[0] as [
      MetadataSearchResult,
      Set<string>,
      boolean,
    ];
    expect(appliedKeys.has("bookName")).toBe(false);
    expect(appliedResult.subtitle).toBeUndefined();
  });

  describe("series", () => {
    const withSeries = (
      ...series: { seriesName: string; seriesPart?: string }[]
    ): MetadataSearchResult => ({ ...searchResult, series });

    const appliedSeriesNames = (onApply: ReturnType<typeof vi.fn>) =>
      (onApply.mock.calls[0]?.[0] as MetadataSearchResult).series.map((s) => s.seriesName);

    it("offers the primary choice only when the source reports more than one series", () => {
      const { unmount } = renderWithQuery(
        <TagPreviewDialog
          open
          onOpenChange={() => {}}
          currentInput={currentInput}
          searchResult={withSeries({ seriesName: "Mistborn", seriesPart: "1" })}
          onApply={() => {}}
        />,
      );
      expect(screen.queryByText("Primary series")).not.toBeInTheDocument();
      unmount();

      renderWithQuery(
        <TagPreviewDialog
          open
          onOpenChange={() => {}}
          currentInput={currentInput}
          searchResult={withSeries({ seriesName: "Mistborn" }, { seriesName: "Cosmere" })}
          onApply={() => {}}
        />,
      );
      expect(screen.getByText("Primary series")).toBeInTheDocument();
    });

    it("defaults the primary to the book's current series when the source still lists it, whatever the source's order", () => {
      const onApply = vi.fn();
      renderWithQuery(
        <TagPreviewDialog
          open
          onOpenChange={() => {}}
          currentInput={{ ...currentInput, series: "Mistborn", seriesPart: "1" }}
          searchResult={withSeries(
            { seriesName: "Cosmere", seriesPart: "3" },
            { seriesName: "Mistborn", seriesPart: "1" },
          )}
          onApply={onApply}
        />,
      );

      expect(screen.getByRole("radio", { name: "Mistborn" })).toBeChecked();
      fireEvent.click(screen.getByText("Apply All"));

      // Handed over primary-first: the consumer applies "first = primary, the rest = additional".
      expect(appliedSeriesNames(onApply)).toEqual(["Mistborn", "Cosmere"]);
    });

    it("hands the chosen primary over first", () => {
      const onApply = vi.fn();
      renderWithQuery(
        <TagPreviewDialog
          open
          onOpenChange={() => {}}
          currentInput={{ ...currentInput, series: "Mistborn", seriesPart: "1" }}
          searchResult={withSeries(
            { seriesName: "Mistborn", seriesPart: "1" },
            { seriesName: "Cosmere", seriesPart: "3" },
          )}
          onApply={onApply}
        />,
      );

      fireEvent.click(screen.getByRole("radio", { name: "Cosmere" }));
      fireEvent.click(screen.getByText("Apply All"));

      expect(appliedSeriesNames(onApply)).toEqual(["Cosmere", "Mistborn"]);
    });

    it("counts the series as a changed field only for a real change, not for the source's ordering", () => {
      const current: OrganizeAudiobookInput = {
        ...currentInput,
        series: "Mistborn",
        seriesPart: "1",
        additionalSeries: [{ seriesName: "Cosmere", seriesPart: "3" }],
      };
      const selectedCount = (result: MetadataSearchResult) => {
        const { unmount } = renderWithQuery(
          <TagPreviewDialog
            open
            onOpenChange={() => {}}
            currentInput={current}
            searchResult={result}
            onApply={() => {}}
          />,
        );
        const match = /Apply Selected \((\d+)\)/.exec(
          screen.getByRole("button", { name: /Apply Selected/ }).textContent ?? "",
        );
        unmount();
        return Number(match?.[1]);
      };

      const sameOrder = selectedCount(
        withSeries(
          { seriesName: "Mistborn", seriesPart: "1" },
          { seriesName: "Cosmere", seriesPart: "3" },
        ),
      );
      const reordered = selectedCount(
        withSeries(
          { seriesName: "Cosmere", seriesPart: "3" },
          { seriesName: "Mistborn", seriesPart: "1" },
        ),
      );
      const partChanged = selectedCount(
        withSeries(
          { seriesName: "Cosmere", seriesPart: "4" },
          { seriesName: "Mistborn", seriesPart: "1" },
        ),
      );
      const removed = selectedCount(withSeries({ seriesName: "Mistborn", seriesPart: "1" }));

      expect(reordered).toBe(sameOrder);
      expect(partChanged).toBe(sameOrder + 1);
      expect(removed).toBe(sameOrder + 1);
    });
  });

  it("offers source qualifiers as their own optional row, independent of the title", () => {
    const onApply = vi.fn();

    renderWithQuery(
      <TagPreviewDialog
        open={true}
        onOpenChange={() => {}}
        currentInput={currentInput}
        searchResult={{ ...searchResult, qualifiers: ["dramatized"] }}
        onApply={onApply}
      />,
    );

    const row = screen.getByText("Qualifiers").closest("tr");
    expect(row).not.toBeNull();
    const checkbox = within(row as HTMLElement).getByRole("checkbox");
    // Changed, so it is selected by default like every other changed field...
    expect(checkbox).toBeChecked();

    // ...and deselecting it leaves the title (already clean of the wording) applying on its own.
    fireEvent.click(checkbox);
    fireEvent.click(screen.getByRole("button", { name: /selected/i }));

    const [, appliedKeys] = onApply.mock.calls[0] as [MetadataSearchResult, Set<string>];
    expect(appliedKeys.has("qualifiers")).toBe(false);
    expect(appliedKeys.has("bookName")).toBe(true);
  });
});
