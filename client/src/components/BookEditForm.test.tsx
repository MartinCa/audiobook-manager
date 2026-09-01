import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BookEditForm } from "./BookEditForm";
import type { Audiobook } from "@/types/Audiobook";

vi.mock("@/services/api", () => ({
  audiobookApi: {
    generateNewPath: vi.fn().mockResolvedValue("Author/2024 - Book/book.m4b"),
  },
  settingsApi: {
    getLanguages: vi.fn().mockResolvedValue({ languages: [] }),
  },
  similarValuesApi: {
    getAuthorNames: vi.fn().mockResolvedValue([]),
    getSeriesNames: vi.fn().mockResolvedValue([]),
  },
  metadataSearchApi: {
    getServices: vi.fn().mockResolvedValue([{ name: "Goodreads", enabled: true }]),
    searchMultiple: vi.fn().mockResolvedValue({ results: [], sourceStatuses: [] }),
  },
}));

function renderWithProviders(ui: React.ReactElement) {
  // A fresh client per render — several tests below vary the getLanguages mock per-call, and a
  // shared client would serve a stale cached "languages" query result across tests.
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

const initialBook: Audiobook = {
  authors: [{ name: "Jane Author" }],
  narrators: [],
  bookName: "Original Title",
  genres: [],
  year: 2020,
};

describe("BookEditForm", () => {
  it("seeds an empty language with the backend default when defaultEmptyLanguage is set", async () => {
    const { settingsApi } = await import("@/services/api");
    vi.mocked(settingsApi.getLanguages).mockResolvedValueOnce({
      languages: [
        { code: "en", displayName: "English", aliases: ["eng"] },
        { code: "da", displayName: "Danish", aliases: ["dansk"] },
      ],
      defaultCode: "en",
    });

    renderWithProviders(
      <BookEditForm initialBook={initialBook} onSave={vi.fn()} defaultEmptyLanguage />,
    );

    expect(await screen.findByText("English")).toBeInTheDocument();
  });

  it("does not default the language when defaultEmptyLanguage is not set", async () => {
    const { settingsApi } = await import("@/services/api");
    vi.mocked(settingsApi.getLanguages).mockResolvedValueOnce({
      languages: [{ code: "en", displayName: "English", aliases: ["eng"] }],
      defaultCode: "en",
    });

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    await waitFor(() => expect(settingsApi.getLanguages).toHaveBeenCalled());
    expect(screen.queryByText("English")).not.toBeInTheDocument();
  });

  it("renders fields populated from the initial book", () => {
    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    expect(screen.getByDisplayValue("Jane Author")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Original Title")).toBeInTheDocument();
    expect(screen.getByDisplayValue("2020")).toBeInTheDocument();
  });

  it("blocks submit and shows a validation error when authors is cleared", async () => {
    const onSave = vi.fn();
    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={onSave} />);

    fireEvent.change(screen.getByDisplayValue("Jane Author"), { target: { value: "" } });
    fireEvent.click(screen.getByText("Save Audiobook"));

    expect(await screen.findByText("At least one author is required")).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
  });

  it("shows a click-to-use hint when a similar author name already exists", async () => {
    const { similarValuesApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getAuthorNames).mockResolvedValueOnce(["Jane Authorr"]);

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    await waitFor(() => expect(similarValuesApi.getAuthorNames).toHaveBeenCalled());

    const authorsInput = screen.getByDisplayValue("Jane Author");
    fireEvent.change(authorsInput, { target: { value: "Jane" } });
    fireEvent.blur(authorsInput);

    expect(
      await screen.findByText("Similar existing author: Jane Authorr (click to use)"),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByText("Similar existing author: Jane Authorr (click to use)"));

    expect(screen.getByDisplayValue("Jane Authorr")).toBeInTheDocument();
  });

  it("does not submit the outer form when the search dialog's own form is submitted", async () => {
    // Regression test: BookSearchDialog's <DialogContent> portals to document.body, but React
    // still bubbles synthetic events through the component tree it's rendered in, not the DOM
    // tree. Without BookSearchDialog stopping propagation on its own form's submit, submitting
    // that search form also submitted (and saved/organized) this outer form.
    const onSave = vi.fn();
    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={onSave} />);

    fireEvent.click(screen.getByText("Search Online Metadata"));

    const searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Some Book" } });
    fireEvent.submit(searchInput.closest("form")!);

    const { metadataSearchApi } = await import("@/services/api");
    await waitFor(() => expect(metadataSearchApi.searchMultiple).toHaveBeenCalled());
    expect(onSave).not.toHaveBeenCalled();
  });

  it("calls onSave with the built audiobook on a valid submit", async () => {
    const onSave = vi.fn();
    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={onSave} />);

    fireEvent.change(screen.getByDisplayValue("Original Title"), {
      target: { value: "Updated Title" },
    });
    fireEvent.click(screen.getByText("Save Audiobook"));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    const saved = onSave.mock.calls[0]?.[0] as Audiobook;
    expect(saved.bookName).toBe("Updated Title");
    expect(saved.authors).toEqual([{ name: "Jane Author" }]);
    expect(saved.year).toBe(2020);
  });

  it("normalizes scraped language and handles null/empty fields safely without crashing on trim", async () => {
    const { metadataSearchApi, settingsApi } = await import("@/services/api");
    vi.mocked(settingsApi.getLanguages).mockResolvedValueOnce({
      languages: [
        { code: "en", displayName: "English", aliases: ["eng", "english"] },
        { code: "da", displayName: "Danish", aliases: ["dansk"] },
      ],
      defaultCode: "en",
    });

    vi.mocked(metadataSearchApi.searchMultiple).mockResolvedValueOnce({
      results: [
        {
          url: "https://audible.com/pd/B09KDG66KL",
          source: "Audible",
          bookName: "Scraped Book",
          authors: [{ name: "Scraped Author" }],
          narrators: [],
          series: [],
          genres: [],
          language: "English",
          description: "A great book",
        },
      ],
      sourceStatuses: [],
    });

    const onSave = vi.fn();
    renderWithProviders(
      <BookEditForm
        initialBook={{
          ...initialBook,
          subtitle: undefined,
          series: undefined,
          seriesPart: undefined,
          description: undefined,
          language: undefined,
        }}
        onSave={onSave}
      />,
    );

    // Open search dialog and search
    fireEvent.click(screen.getByText("Search Online Metadata"));
    const searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Scraped" } });
    fireEvent.submit(searchInput.closest("form")!);

    // Apply result from search
    const applyButton = await screen.findByRole("button", { name: "Apply" });
    fireEvent.click(applyButton);

    // Tag preview dialog opens, click Apply All
    const applyAllButton = await screen.findByRole("button", { name: "Apply All" });
    fireEvent.click(applyAllButton);

    // Language should be normalized to English (code: en)
    expect(await screen.findByText("English")).toBeInTheDocument();
    expect(screen.queryByText("english (unrecognized)")).not.toBeInTheDocument();

    // Submit form and verify saved structure
    fireEvent.click(screen.getByText("Save Audiobook"));
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    const saved = onSave.mock.calls[0]?.[0] as Audiobook;
    expect(saved.language).toBe("en");
    expect(saved.bookName).toBe("Scraped Book");
  });

  it("renders delete button when onDelete is provided and triggers callback on click", () => {
    const onDelete = vi.fn();
    renderWithProviders(
      <BookEditForm
        initialBook={initialBook}
        onSave={vi.fn()}
        onDelete={onDelete}
        deleteLabel="Delete Audiobook"
      />,
    );

    const deleteBtn = screen.getByRole("button", { name: "Delete Audiobook" });
    expect(deleteBtn).toBeInTheDocument();

    fireEvent.click(deleteBtn);
    expect(onDelete).toHaveBeenCalledTimes(1);
  });

  it("renders custom submitLabel and disables action buttons when isSaving is true", () => {
    const onDelete = vi.fn();
    renderWithProviders(
      <BookEditForm
        initialBook={initialBook}
        onSave={vi.fn()}
        onDelete={onDelete}
        submitLabel="Organize into Library"
        isSaving={true}
      />,
    );

    const submitBtn = screen.getByRole("button", { name: "Organize into Library" });
    expect(submitBtn).toBeDisabled();

    const deleteBtn = screen.getByRole("button", { name: "Delete File" });
    expect(deleteBtn).toBeDisabled();

    const resetBtn = screen.getByRole("button", { name: "Reset" });
    expect(resetBtn).toBeDisabled();
  });

  it("offers live author typeahead suggestions while typing and selects on click", async () => {
    const { similarValuesApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getAuthorNames).mockResolvedValueOnce([
      "Brandon Sanderson",
      "Patrick Rothfuss",
    ]);

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    await waitFor(() => expect(similarValuesApi.getAuthorNames).toHaveBeenCalled());

    const authorsInput = screen.getByDisplayValue("Jane Author");
    fireEvent.focus(authorsInput);
    fireEvent.change(authorsInput, { target: { value: "Sand" } });

    expect(await screen.findByRole("option", { name: "Brandon Sanderson" })).toBeInTheDocument();

    fireEvent.pointerDown(screen.getByRole("option", { name: "Brandon Sanderson" }));
    expect(authorsInput).toHaveValue("Brandon Sanderson");
  });

  it("offers live series typeahead suggestions while typing and selects on click", async () => {
    const { similarValuesApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getSeriesNames).mockResolvedValueOnce([
      "The Stormlight Archive",
      "Mistborn",
    ]);

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    await waitFor(() => expect(similarValuesApi.getSeriesNames).toHaveBeenCalled());

    const seriesInput = screen.getByPlaceholderText("Series name");
    fireEvent.focus(seriesInput);
    fireEvent.change(seriesInput, { target: { value: "Storm" } });

    expect(
      await screen.findByRole("option", { name: "The Stormlight Archive" }),
    ).toBeInTheDocument();

    fireEvent.pointerDown(screen.getByRole("option", { name: "The Stormlight Archive" }));
    expect(seriesInput).toHaveValue("The Stormlight Archive");
  });
});
