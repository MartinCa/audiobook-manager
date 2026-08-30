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

  it("calls onApply with all keys when Apply All is clicked", () => {
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

    const applyAllBtn = screen.getByText("Apply All");
    fireEvent.click(applyAllBtn);

    expect(onApply).toHaveBeenCalledTimes(1);
    const [, appliedKeys] = onApply.mock.calls[0] as [MetadataSearchResult, Set<string>];
    expect(appliedKeys.has("bookName")).toBe(true);
    expect(appliedKeys.has("authors")).toBe(true);
  });
});
