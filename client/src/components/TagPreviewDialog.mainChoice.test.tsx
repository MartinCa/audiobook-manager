import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { TagPreviewDialog } from "./TagPreviewDialog";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";
import type { MetadataChoiceRules } from "@/types/MetadataChoiceRules";

vi.mock("@/services/api", () => ({
  settingsApi: {
    getLanguages: vi.fn().mockResolvedValue({ languages: [], defaultCode: "en" }),
    getMetadataApplyRules: vi.fn().mockResolvedValue({
      fields: [],
      interactiveOptions: [],
      automatedOptions: [],
    }),
    getMetadataChoiceRules: vi.fn(),
    getBookQualifiers: vi.fn().mockResolvedValue({ qualifiers: [] }),
  },
}));

import { settingsApi } from "@/services/api";

function choiceRules(
  whenSourceNamesMain: "KeepCurrent" | "UseSources",
  choice: "MainAuthor" | "PrimarySeries",
): MetadataChoiceRules {
  return {
    choices: [
      {
        choice,
        label: choice,
        description: "",
        whenSourceNamesMain,
        whenSourceSilent: "KeepCurrent",
        namesMainOptions: [],
        silentOptions: [],
      },
    ],
  };
}

const current: OrganizeAudiobookInput = {
  bookName: "Same Title",
  authors: "A One",
  mainAuthor: "A One",
  series: "Saga",
  year: 2020,
};

const source: MetadataSearchResult = {
  url: "https://example.com/b",
  cleanUrl: "https://example.com/b",
  source: "Goodreads",
  bookName: "Same Title",
  authors: [{ name: "A One" }, { name: "B Two" }],
  mainAuthor: "B Two",
  narrators: [],
  series: [{ seriesName: "Saga" }, { seriesName: "Cosmere" }],
  mainSeries: "Cosmere",
  genres: [],
  year: 2020,
};

function renderDialog(onApply = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <TagPreviewDialog
        open
        onOpenChange={() => {}}
        currentInput={current}
        searchResult={source}
        onApply={onApply}
      />
    </QueryClientProvider>,
  );
  return onApply;
}

function appliedResult(onApply: ReturnType<typeof vi.fn>): MetadataSearchResult {
  return (onApply.mock.calls[0] as [MetadataSearchResult])[0];
}

describe("TagPreviewDialog main author and primary series choice", () => {
  beforeEach(() => {
    vi.mocked(settingsApi.getMetadataChoiceRules).mockReset().mockResolvedValue({ choices: [] });
  });

  it("keeps the book's own main author and primary series by default, though the source names others", async () => {
    const onApply = renderDialog();

    expect(await screen.findByRole("radio", { name: "A One" })).toBeChecked();
    fireEvent.click(screen.getByText("Apply All"));

    const result = appliedResult(onApply);
    expect(result.mainAuthor).toBe("A One");
    expect(result.series.map((s) => s.seriesName)).toEqual(["Saga", "Cosmere"]);
  });

  it("preselects and applies the source's main author when the rule is Use the source's", async () => {
    vi.mocked(settingsApi.getMetadataChoiceRules).mockResolvedValue(
      choiceRules("UseSources", "MainAuthor"),
    );
    const onApply = renderDialog();

    await waitFor(() => expect(screen.getByRole("radio", { name: "B Two" })).toBeChecked());
    fireEvent.click(screen.getByText("Apply All"));

    expect(appliedResult(onApply).mainAuthor).toBe("B Two");
  });

  it("preselects and files under the source's featured series when the rule is Use the source's", async () => {
    vi.mocked(settingsApi.getMetadataChoiceRules).mockResolvedValue(
      choiceRules("UseSources", "PrimarySeries"),
    );
    const onApply = renderDialog();

    await waitFor(() => expect(screen.getByRole("radio", { name: "Cosmere" })).toBeChecked());
    fireEvent.click(screen.getByText("Apply All"));

    expect(appliedResult(onApply).series.map((s) => s.seriesName)).toEqual(["Cosmere", "Saga"]);
  });

  it("lets a manual pick beat the rule, and No main author applies as empty", async () => {
    vi.mocked(settingsApi.getMetadataChoiceRules).mockResolvedValue(
      choiceRules("UseSources", "MainAuthor"),
    );
    const onApply = renderDialog();

    await waitFor(() => expect(screen.getByRole("radio", { name: "B Two" })).toBeChecked());
    fireEvent.click(screen.getByRole("radio", { name: "No main author" }));
    fireEvent.click(screen.getByText("Apply All"));

    expect(appliedResult(onApply).mainAuthor).toBe("");
  });

  it("explains an Ask me rule next to the picker", async () => {
    const askMe = choiceRules("UseSources", "MainAuthor");
    vi.mocked(settingsApi.getMetadataChoiceRules).mockResolvedValue({
      choices: askMe.choices.map((c) => ({ ...c, whenSourceNamesMain: "AskMe" as const })),
    });
    renderDialog();

    expect(
      await screen.findByText(/library settings ask you to confirm the main author/i),
    ).toBeInTheDocument();
  });
});
