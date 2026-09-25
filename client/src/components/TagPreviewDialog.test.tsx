import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
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
});
