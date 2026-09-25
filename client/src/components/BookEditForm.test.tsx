import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BookEditForm } from "./BookEditForm";
import type { Audiobook } from "@/types/Audiobook";

vi.mock("@/services/api", () => ({
  audiobookApi: {
    generateNewPath: vi.fn().mockResolvedValue("Author/2024 - Book/book.m4b"),
    getSeriesPartConflicts: vi.fn().mockResolvedValue({ conflicts: [], truncated: false }),
  },
  settingsApi: {
    getLanguages: vi.fn().mockResolvedValue({ languages: [] }),
  },
  similarValuesApi: {
    getAutocomplete: vi.fn().mockResolvedValue([]),
    getEntryStatus: vi.fn().mockResolvedValue({
      value: "",
      status: "new",
      exactMatch: null,
      similarMatches: [],
    }),
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

    expect(screen.getByText("Jane Author")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Original Title")).toBeInTheDocument();
    expect(screen.getByDisplayValue("2020")).toBeInTheDocument();
  });

  it("blocks submit and shows a validation error when authors is cleared", async () => {
    const onSave = vi.fn();
    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={onSave} />);

    fireEvent.click(screen.getByLabelText("Remove Jane Author"));
    fireEvent.click(screen.getByText("Save Audiobook"));

    expect(await screen.findByText("At least one author is required")).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
  });

  it("shows a click-to-use hint for the initial author as soon as the server classifies it as similar, with no typing required", async () => {
    const { similarValuesApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getEntryStatus).mockImplementation((_valueType, value) =>
      Promise.resolve({
        value,
        status: value === "Jane Author" ? "similar" : "new",
        exactMatch: null,
        similarMatches: [{ id: 5, name: "Jane Authorr" }],
      }),
    );

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    // "Jane Author" (already on the book) comes back classified similar to the existing
    // "Jane Authorr" - the hint must appear purely from the server response arriving, without
    // the user typing anything.
    expect(await screen.findByText('Similar to "Jane Authorr" — click to use')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Similar to "Jane Authorr" — click to use'));

    expect(screen.getByText("Jane Authorr")).toBeInTheDocument();
    expect(screen.queryByText('Similar to "Jane Authorr" — click to use')).not.toBeInTheDocument();
  });

  it("shows an independent hint for every flagged author at once, not just the most recently typed one", async () => {
    const { similarValuesApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getEntryStatus).mockImplementation((_valueType, value) =>
      Promise.resolve({
        value,
        status: "similar",
        exactMatch: null,
        similarMatches: [
          { id: 5, name: value === "Jane Author" ? "Jane Authorr" : "Brandon Sanderson" },
        ],
      }),
    );

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);
    await screen.findByText('Similar to "Jane Authorr" — click to use');

    // Committing a second, unrelated typo'd author must not clear the first author's hint - the
    // bug this regresses against always overwrote a single hint slot with the latest commit.
    const authorsInput = screen.getByRole("textbox", { name: "Author Name, Second Author" });
    fireEvent.change(authorsInput, { target: { value: "Brandon Sandersonn" } });
    fireEvent.keyDown(authorsInput, { key: "Escape" });
    fireEvent.keyDown(authorsInput, { key: "Enter" });

    expect(
      await screen.findByText('Similar to "Brandon Sanderson" — click to use'),
    ).toBeInTheDocument();
    expect(screen.getByText('Similar to "Jane Authorr" — click to use')).toBeInTheDocument();

    // Clicking one hint only fixes the entry it describes.
    fireEvent.click(screen.getByText('Similar to "Brandon Sanderson" — click to use'));

    expect(screen.getByText("Brandon Sanderson")).toBeInTheDocument();
    expect(screen.getByText("Jane Author")).toBeInTheDocument();
    expect(screen.getByText('Similar to "Jane Authorr" — click to use')).toBeInTheDocument();
  });

  it("clicking a hint merges into an existing exact-match entry instead of creating a duplicate", async () => {
    const { similarValuesApi } = await import("@/services/api");
    // "Brandon Sanderson" is already a separate author on this book, and also the suggestion
    // for the "Brandon Sandersons" typo - clicking the hint must not leave two identical
    // "Brandon Sanderson" chips (which broke drag-and-drop and could double the credited author).
    vi.mocked(similarValuesApi.getEntryStatus).mockImplementation((_valueType, value) =>
      Promise.resolve({
        value,
        status: value === "Brandon Sanderson" ? "exact" : "similar",
        exactMatch: value === "Brandon Sanderson" ? { id: 5, name: "Brandon Sanderson" } : null,
        similarMatches:
          value === "Brandon Sandersons" ? [{ id: 5, name: "Brandon Sanderson" }] : [],
      }),
    );

    renderWithProviders(
      <BookEditForm
        initialBook={{
          ...initialBook,
          authors: [{ name: "Brandon Sanderson" }, { name: "Brandon Sandersons" }],
        }}
        onSave={vi.fn()}
      />,
    );

    await screen.findByText('Similar to "Brandon Sanderson" — click to use');

    fireEvent.click(screen.getByText('Similar to "Brandon Sanderson" — click to use'));

    expect(screen.getAllByLabelText("Remove Brandon Sanderson")).toHaveLength(1);
    expect(screen.queryByText("Brandon Sandersons")).not.toBeInTheDocument();
    expect(
      screen.queryByText('Similar to "Brandon Sanderson" — click to use'),
    ).not.toBeInTheDocument();
  });

  it("clears the author hint once the flagged entry is fixed by editing it directly", async () => {
    const { similarValuesApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getEntryStatus).mockImplementation((_valueType, value) =>
      Promise.resolve({
        value,
        status: value === "Jane Author" ? "similar" : "new",
        exactMatch: null,
        similarMatches: value === "Jane Author" ? [{ id: 5, name: "Jane Authorr" }] : [],
      }),
    );

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);
    await screen.findByText('Similar to "Jane Authorr" — click to use');

    fireEvent.click(screen.getByLabelText("Edit Jane Author"));
    const editInput = screen.getByDisplayValue("Jane Author");
    fireEvent.change(editInput, { target: { value: "Someone Else Entirely" } });
    fireEvent.keyDown(editInput, { key: "Enter" });

    expect(screen.queryByText('Similar to "Jane Authorr" — click to use')).not.toBeInTheDocument();
    expect(screen.getByText("Someone Else Entirely")).toBeInTheDocument();
  });

  it("clears the author hint once the flagged entry is removed", async () => {
    const { similarValuesApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getEntryStatus).mockResolvedValueOnce({
      value: "Jane Author",
      status: "similar",
      exactMatch: null,
      similarMatches: [{ id: 5, name: "Jane Authorr" }],
    });

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);
    await screen.findByText('Similar to "Jane Authorr" — click to use');

    fireEvent.click(screen.getByLabelText("Remove Jane Author"));

    expect(screen.queryByText('Similar to "Jane Authorr" — click to use')).not.toBeInTheDocument();
  });

  it("shows a hint for an author applied from a scraped metadata search result, not just hand-typed entries", async () => {
    const { similarValuesApi, metadataSearchApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getEntryStatus).mockImplementation((_valueType, value) =>
      Promise.resolve({
        value,
        status: value === "Brandon Sandersonn" ? "similar" : "new",
        exactMatch: null,
        similarMatches:
          value === "Brandon Sandersonn" ? [{ id: 5, name: "Brandon Sanderson" }] : [],
      }),
    );
    vi.mocked(metadataSearchApi.searchMultiple).mockResolvedValueOnce({
      results: [
        {
          url: "https://audible.com/pd/B09KDG66KL",
          cleanUrl: "https://audible.com/pd/B09KDG66KL",
          source: "Audible",
          bookName: "Scraped Book",
          authors: [{ name: "Brandon Sandersonn" }],
          narrators: [],
          series: [],
          genres: [],
        },
      ],
      sourceStatuses: [],
    });

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    fireEvent.click(screen.getByText("Search Online Metadata"));
    const searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Scraped" } });
    fireEvent.submit(searchInput.closest("form")!);

    const applyButton = await screen.findByRole("button", { name: "Apply" });
    fireEvent.click(applyButton);
    const applyAllButton = await screen.findByRole("button", { name: "Apply & Save All" });
    fireEvent.click(applyAllButton);

    // The bulk-applied author never goes through TagsInput's own draft-entry flow, so this only
    // works because the indicator is derived from the current field value, not from a "just typed"
    // event.
    expect(
      await screen.findByText('Similar to "Brandon Sanderson" — click to use'),
    ).toBeInTheDocument();
  });

  // The applied-search signal is bookkeeping the backend stamps LastMetadataRefreshedAt from:
  // it must ride exactly the save that carried the applied search result, and nothing else.
  it("sends metadataAppliedFromSearch with the auto-save that follows applying a search result, then clears it for the next save", async () => {
    const { metadataSearchApi } = await import("@/services/api");
    vi.mocked(metadataSearchApi.searchMultiple).mockResolvedValueOnce({
      results: [
        {
          url: "https://audible.com/pd/B09KDG66KL",
          cleanUrl: "https://audible.com/pd/B09KDG66KL",
          source: "Audible",
          bookName: "Scraped Book",
          authors: [{ name: "Jane Author" }],
          narrators: [],
          series: [],
          genres: [],
        },
      ],
      sourceStatuses: [],
    });

    const onSave = vi.fn<(book: Audiobook) => Promise<void>>().mockResolvedValue(undefined);
    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={onSave} />);

    // No search applied yet: a plain save must not carry the signal.
    fireEvent.click(screen.getByText("Save Audiobook"));
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave.mock.calls[0]?.[0].metadataAppliedFromSearch).toBe(false);

    // Apply a search result — with the auto-save opt-out toggle left at its default (off), this
    // saves immediately, with no separate manual Save click.
    fireEvent.click(screen.getByText("Search Online Metadata"));
    const searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Scraped" } });
    fireEvent.submit(searchInput.closest("form")!);

    const applyButton = await screen.findByRole("button", { name: "Apply" });
    fireEvent.click(applyButton);
    const applyAllButton = await screen.findByRole("button", { name: "Apply & Save All" });
    fireEvent.click(applyAllButton);

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(2));
    expect(onSave.mock.calls[1]?.[0].metadataAppliedFromSearch).toBe(true);
    expect(onSave.mock.calls[1]?.[0].bookName).toBe("Scraped Book");

    // One-shot: the following plain save must not re-stamp the refresh timestamp.
    fireEvent.click(screen.getByText("Save Audiobook"));
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(3));
    expect(onSave.mock.calls[2]?.[0].metadataAppliedFromSearch).toBe(false);
  });

  // The auto-save toggle is the opt-out for the new default behavior (item 1): checking it means
  // "just apply to the edit form for review, like before" - and it must default to off (i.e. save
  // immediately) on every fresh apply flow, never remembering a previous flow's choice.
  it("does not auto-save when the don't-save-automatically toggle is checked, and defaults back to off on the next flow", async () => {
    const { metadataSearchApi } = await import("@/services/api");
    // Two distinct result objects (not the same mockResolvedValue reused): TagPreviewDialog resets
    // its toggle by comparing the incoming searchResult against the last one it saw, so the test
    // needs a second flow whose result is a genuinely different object, matching what a real
    // second search response would be.
    const scrapedResult = {
      url: "https://audible.com/pd/B09KDG66KL",
      cleanUrl: "https://audible.com/pd/B09KDG66KL",
      source: "Audible",
      bookName: "Scraped Book",
      authors: [{ name: "Jane Author" }],
      narrators: [],
      series: [],
      genres: [],
    };
    vi.mocked(metadataSearchApi.searchMultiple)
      .mockResolvedValueOnce({ results: [scrapedResult], sourceStatuses: [] })
      .mockResolvedValueOnce({ results: [{ ...scrapedResult }], sourceStatuses: [] });

    const onSave = vi.fn<(book: Audiobook) => Promise<void>>().mockResolvedValue(undefined);
    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={onSave} />);

    fireEvent.click(screen.getByText("Search Online Metadata"));
    let searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Scraped" } });
    fireEvent.submit(searchInput.closest("form")!);
    let applyButton = await screen.findByRole("button", { name: "Apply" });
    fireEvent.click(applyButton);

    // Opt out of auto-save for this flow.
    fireEvent.click(screen.getByRole("checkbox", { name: "Don't save automatically" }));
    const applyAllButton = await screen.findByRole("button", { name: "Apply All" });
    fireEvent.click(applyAllButton);

    // Fields are populated, but nothing was saved.
    expect(await screen.findByDisplayValue("Scraped Book")).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();

    // A manual save is still required, and still carries the applied-search signal.
    fireEvent.click(screen.getByText("Save Audiobook"));
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave.mock.calls[0]?.[0].metadataAppliedFromSearch).toBe(true);

    // A new flow must not remember the previous one's toggle state: this Apply & Save All click
    // saves immediately again, with no toggle interaction.
    fireEvent.click(screen.getByText("Search Online Metadata"));
    searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Scraped" } });
    fireEvent.submit(searchInput.closest("form")!);
    applyButton = await screen.findByRole("button", { name: "Apply" });
    fireEvent.click(applyButton);
    const secondApplyAllButton = await screen.findByRole("button", { name: "Apply & Save All" });
    fireEvent.click(secondApplyAllButton);

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(2));
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
          cleanUrl: "https://audible.com/pd/B09KDG66KL",
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

    // Tag preview dialog opens; Apply & Save All applies the fields and, since the auto-save
    // toggle is off by default, saves immediately without a separate manual Save click.
    const applyAllButton = await screen.findByRole("button", { name: "Apply & Save All" });
    fireEvent.click(applyAllButton);

    // Language should be normalized to English (code: en)
    expect(await screen.findByText("English")).toBeInTheDocument();
    expect(screen.queryByText("english (unrecognized)")).not.toBeInTheDocument();

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

  it("offers live author typeahead suggestions while typing and commits the selection as a chip", async () => {
    const { similarValuesApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getAutocomplete).mockResolvedValueOnce([
      "Brandon Sanderson",
      "Patrick Rothfuss",
    ]);

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    const authorsInput = screen.getByRole("textbox", { name: "Author Name, Second Author" });
    fireEvent.focus(authorsInput);
    fireEvent.change(authorsInput, { target: { value: "Sand" } });

    // The suggestions are a bounded server-side lookup of the typed query, not a preloaded list.
    await waitFor(() =>
      expect(similarValuesApi.getAutocomplete).toHaveBeenCalledWith("author", "Sand", 6),
    );
    expect(await screen.findByRole("option", { name: "Brandon Sanderson" })).toBeInTheDocument();

    fireEvent.pointerDown(screen.getByRole("option", { name: "Brandon Sanderson" }));

    expect(screen.getByText("Brandon Sanderson")).toBeInTheDocument();
    expect(authorsInput).toHaveValue("");
  });

  it("offers live narrator typeahead suggestions through the bounded server-side lookup, matching author parity", async () => {
    const { similarValuesApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getAutocomplete).mockResolvedValue([
      "Michael Kramerr",
      "Kate Reading",
    ]);

    renderWithProviders(
      <BookEditForm
        initialBook={{ ...initialBook, narrators: [{ name: "Michael Kramer" }] }}
        onSave={vi.fn()}
      />,
    );

    const narratorsInput = screen.getByRole("textbox", { name: "Narrator Name" });
    fireEvent.change(narratorsInput, { target: { value: "Kate" } });

    // The suggestions are a bounded server-side lookup of the typed query, not a preloaded list.
    await waitFor(() =>
      expect(similarValuesApi.getAutocomplete).toHaveBeenCalledWith("narrator", "Kate", 6),
    );
    expect(await screen.findByRole("option", { name: "Kate Reading" })).toBeInTheDocument();
    fireEvent.pointerDown(screen.getByRole("option", { name: "Kate Reading" }));

    expect(screen.getByText("Kate Reading")).toBeInTheDocument();
  });

  it("shows a click-to-use hint for the initial narrator as soon as the server classifies it as similar, matching author parity", async () => {
    const { similarValuesApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getEntryStatus).mockImplementation((_valueType, value) =>
      Promise.resolve({
        value,
        status: value === "Michael Kramer" ? "similar" : "new",
        exactMatch: null,
        similarMatches: value === "Michael Kramer" ? [{ id: 2, name: "Michael Kramerr" }] : [],
      }),
    );

    renderWithProviders(
      <BookEditForm
        initialBook={{ ...initialBook, narrators: [{ name: "Michael Kramer" }] }}
        onSave={vi.fn()}
      />,
    );

    expect(
      await screen.findByText('Similar to "Michael Kramerr" — click to use'),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByText('Similar to "Michael Kramerr" — click to use'));

    expect(screen.getByText("Michael Kramerr")).toBeInTheDocument();
  });

  it("reordering authors by drag does not affect the array via edit/remove side effects", () => {
    renderWithProviders(
      <BookEditForm
        initialBook={{
          ...initialBook,
          authors: [{ name: "Alpha Author" }, { name: "Beta Author" }],
        }}
        onSave={vi.fn()}
      />,
    );

    // Editing one author must not reorder the other - the drag handle is the only reorder path.
    fireEvent.click(screen.getByLabelText("Edit Alpha Author"));
    const editInput = screen.getByDisplayValue("Alpha Author");
    fireEvent.change(editInput, { target: { value: "Alpha Author Fixed" } });
    fireEvent.keyDown(editInput, { key: "Enter" });

    expect(screen.getByText("Alpha Author Fixed")).toBeInTheDocument();
    expect(screen.getByText("Beta Author")).toBeInTheDocument();
    expect(screen.getByLabelText("Reorder: Alpha Author Fixed")).toBeInTheDocument();
    expect(screen.getByLabelText("Reorder: Beta Author")).toBeInTheDocument();
  });

  it("offers live series typeahead suggestions while typing and selects on click", async () => {
    const { similarValuesApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getAutocomplete).mockResolvedValueOnce([
      "The Stormlight Archive",
      "Mistborn",
    ]);

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    const seriesInput = screen.getByPlaceholderText("Series name");
    fireEvent.focus(seriesInput);
    fireEvent.change(seriesInput, { target: { value: "Storm" } });

    // The suggestions are a bounded server-side lookup of the typed query, not a preloaded list.
    await waitFor(() =>
      expect(similarValuesApi.getAutocomplete).toHaveBeenCalledWith("series", "Storm", 6),
    );
    expect(
      await screen.findByRole("option", { name: "The Stormlight Archive" }),
    ).toBeInTheDocument();

    fireEvent.pointerDown(screen.getByRole("option", { name: "The Stormlight Archive" }));
    expect(seriesInput).toHaveValue("The Stormlight Archive");
  });

  it("shows a click-to-use hint for a similar existing series, including when the value is bulk-applied from a scraped result", async () => {
    const { similarValuesApi, metadataSearchApi } = await import("@/services/api");
    vi.mocked(similarValuesApi.getEntryStatus).mockImplementation((_valueType, value) =>
      Promise.resolve({
        value,
        status: value === "The Stormlight Archives" ? "similar" : "new",
        exactMatch: null,
        similarMatches:
          value === "The Stormlight Archives" ? [{ id: null, name: "The Stormlight Archive" }] : [],
      }),
    );
    vi.mocked(metadataSearchApi.searchMultiple).mockResolvedValueOnce({
      results: [
        {
          url: "https://audible.com/pd/B09KDG66KL",
          cleanUrl: "https://audible.com/pd/B09KDG66KL",
          source: "Audible",
          bookName: "Scraped Book",
          authors: [],
          narrators: [],
          genres: [],
          series: [{ seriesName: "The Stormlight Archives", seriesPart: "1" }],
        },
      ],
      sourceStatuses: [],
    });

    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    // Same gap the author/narrator hint had: a series set in bulk from a metadata-search apply
    // never goes through a manual blur, so the hint must be derived from the field value, not
    // fired only from an onBlur handler.
    fireEvent.click(screen.getByText("Search Online Metadata"));
    const searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Scraped" } });
    fireEvent.submit(searchInput.closest("form")!);
    const applyButton = await screen.findByRole("button", { name: "Apply" });
    fireEvent.click(applyButton);
    const applyAllButton = await screen.findByRole("button", { name: "Apply & Save All" });
    fireEvent.click(applyAllButton);

    expect(
      await screen.findByText('Similar to "The Stormlight Archive" — click to use'),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByText('Similar to "The Stormlight Archive" — click to use'));

    expect(screen.getByDisplayValue("The Stormlight Archive")).toBeInTheDocument();
  });

  it("hides empty optional fields by default and expands them when toggle button is clicked", () => {
    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    // Primary fields are present
    expect(screen.getByText("Jane Author")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Original Title")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Fantasy, Fiction")).toBeInTheDocument();

    // Secondary fields are hidden by default when empty
    expect(screen.queryByPlaceholderText("Narrator Name")).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Subtitle")).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Publisher")).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Copyright year / owner")).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText("e.g. 4.5")).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText("B0...")).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText("https://...")).not.toBeInTheDocument();

    // Toggle button displays count of hidden fields
    const toggleBtn = screen.getByRole("button", { name: /Show additional fields \(7 hidden\)/i });
    expect(toggleBtn).toBeInTheDocument();

    // Click toggle button to expand
    fireEvent.click(toggleBtn);

    // Now all secondary fields are visible
    expect(screen.getByPlaceholderText("Narrator Name")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Subtitle")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Publisher")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Copyright year / owner")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("e.g. 4.5")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("B0...")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("https://...")).toBeInTheDocument();

    // Toggle button now allows hiding empty fields
    const hideBtn = screen.getByRole("button", { name: /Hide empty optional fields/i });
    expect(hideBtn).toBeInTheDocument();

    fireEvent.click(hideBtn);
    expect(screen.queryByPlaceholderText("Narrator Name")).not.toBeInTheDocument();
  });

  it("automatically displays optional fields that have non-empty values", () => {
    renderWithProviders(
      <BookEditForm
        initialBook={{
          ...initialBook,
          narrators: [{ name: "Michael Kramer" }],
          subtitle: "A Great Story",
          publisher: "Tor Books",
          rating: "4.8",
        }}
        onSave={vi.fn()}
      />,
    );

    // Populated optional fields are visible
    expect(screen.getByText("Michael Kramer")).toBeInTheDocument();
    expect(screen.getByDisplayValue("A Great Story")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Tor Books")).toBeInTheDocument();
    expect(screen.getByDisplayValue("4.8")).toBeInTheDocument();

    // Empty optional fields remain hidden
    expect(screen.queryByPlaceholderText("Copyright year / owner")).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText("B0...")).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText("https://...")).not.toBeInTheDocument();

    // Remaining hidden count is 3 (copyright, asin, www)
    expect(
      screen.getByRole("button", { name: /Show additional fields \(3 hidden\)/i }),
    ).toBeInTheDocument();
  });

  // Regression test for Issue C: the backend differ reports an empty source list as a real
  // diff (e.g. "Narrators: X -> (empty)") and TagPreviewDialog shows it as changed, but the old
  // apply guard (`result.narrators.length > 0`) silently kept the stale value instead of
  // clearing it - Apply All must actually clear a field the dialog displayed as blanked.
  it("clears narrators when Apply All is used with a scraped result reporting no narrators", async () => {
    const { metadataSearchApi } = await import("@/services/api");
    vi.mocked(metadataSearchApi.searchMultiple).mockResolvedValueOnce({
      results: [
        {
          url: "https://audible.com/pd/B09KDG66KL",
          cleanUrl: "https://audible.com/pd/B09KDG66KL",
          source: "Audible",
          bookName: "Scraped Book",
          authors: [{ name: "Jane Author" }],
          narrators: [],
          series: [],
          genres: [],
        },
      ],
      sourceStatuses: [],
    });

    const onSave = vi.fn<(book: Audiobook) => Promise<void>>().mockResolvedValue(undefined);
    renderWithProviders(
      <BookEditForm
        initialBook={{ ...initialBook, narrators: [{ name: "Michael Kramer" }] }}
        onSave={onSave}
      />,
    );

    expect(screen.getByText("Michael Kramer")).toBeInTheDocument();

    fireEvent.click(screen.getByText("Search Online Metadata"));
    const searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Scraped" } });
    fireEvent.submit(searchInput.closest("form")!);

    const applyButton = await screen.findByRole("button", { name: "Apply" });
    fireEvent.click(applyButton);
    // Apply & Save All applies the fields and, since the auto-save toggle is off by default,
    // saves immediately without a separate manual Save click.
    const applyAllButton = await screen.findByRole("button", { name: "Apply & Save All" });
    fireEvent.click(applyAllButton);

    // The narrator chip is gone; the field, now empty, collapses back under "additional fields".
    await waitFor(() => expect(screen.queryByText("Michael Kramer")).not.toBeInTheDocument());

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave.mock.calls[0]?.[0].narrators).toEqual([]);
  });

  // Regression test for Issue C: an empty series array from the source must clear both Series
  // and Series Part, the same way the dialog displays "-> (empty)" for the field.
  it("clears series and seriesPart when Apply All is used with a scraped result reporting no series", async () => {
    const { metadataSearchApi } = await import("@/services/api");
    vi.mocked(metadataSearchApi.searchMultiple).mockResolvedValueOnce({
      results: [
        {
          url: "https://audible.com/pd/B09KDG66KL",
          cleanUrl: "https://audible.com/pd/B09KDG66KL",
          source: "Audible",
          bookName: "Scraped Book",
          authors: [{ name: "Jane Author" }],
          narrators: [],
          series: [],
          genres: [],
        },
      ],
      sourceStatuses: [],
    });

    const onSave = vi.fn<(book: Audiobook) => Promise<void>>().mockResolvedValue(undefined);
    renderWithProviders(
      <BookEditForm
        initialBook={{ ...initialBook, series: "The Stormlight Archive", seriesPart: "1" }}
        onSave={onSave}
      />,
    );

    expect(screen.getByDisplayValue("The Stormlight Archive")).toBeInTheDocument();

    fireEvent.click(screen.getByText("Search Online Metadata"));
    const searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Scraped" } });
    fireEvent.submit(searchInput.closest("form")!);

    const applyButton = await screen.findByRole("button", { name: "Apply" });
    fireEvent.click(applyButton);
    // Apply & Save All applies the fields and, since the auto-save toggle is off by default,
    // saves immediately without a separate manual Save click.
    const applyAllButton = await screen.findByRole("button", { name: "Apply & Save All" });
    fireEvent.click(applyAllButton);

    await waitFor(() =>
      expect(screen.queryByDisplayValue("The Stormlight Archive")).not.toBeInTheDocument(),
    );
    expect(screen.getByPlaceholderText("Series name")).toHaveValue("");

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave.mock.calls[0]?.[0].series).toBeUndefined();
    expect(onSave.mock.calls[0]?.[0].seriesPart).toBeUndefined();
  });

  // Regression test for Issue C: a scalar text field (subtitle here) with no source value must
  // be cleared too, not left at its stale current value - matches the dialog's own "-> (empty)".
  it("clears subtitle when Apply All is used with a scraped result reporting no subtitle", async () => {
    const { metadataSearchApi } = await import("@/services/api");
    vi.mocked(metadataSearchApi.searchMultiple).mockResolvedValueOnce({
      results: [
        {
          url: "https://audible.com/pd/B09KDG66KL",
          cleanUrl: "https://audible.com/pd/B09KDG66KL",
          source: "Audible",
          bookName: "Scraped Book",
          authors: [{ name: "Jane Author" }],
          narrators: [],
          series: [],
          genres: [],
        },
      ],
      sourceStatuses: [],
    });

    const onSave = vi.fn<(book: Audiobook) => Promise<void>>().mockResolvedValue(undefined);
    renderWithProviders(
      <BookEditForm
        initialBook={{ ...initialBook, subtitle: "A Great Subtitle" }}
        onSave={onSave}
      />,
    );

    expect(screen.getByDisplayValue("A Great Subtitle")).toBeInTheDocument();

    fireEvent.click(screen.getByText("Search Online Metadata"));
    const searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Scraped" } });
    fireEvent.submit(searchInput.closest("form")!);

    const applyButton = await screen.findByRole("button", { name: "Apply" });
    fireEvent.click(applyButton);
    // Apply & Save All applies the fields and, since the auto-save toggle is off by default,
    // saves immediately without a separate manual Save click.
    const applyAllButton = await screen.findByRole("button", { name: "Apply & Save All" });
    fireEvent.click(applyAllButton);

    await waitFor(() =>
      expect(screen.queryByDisplayValue("A Great Subtitle")).not.toBeInTheDocument(),
    );

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave.mock.calls[0]?.[0].subtitle).toBeUndefined();
  });

  it("only displays File Location / Target Path when target differs from current path", async () => {
    const { audiobookApi } = await import("@/services/api");
    vi.mocked(audiobookApi.generateNewPath).mockImplementation((book) => {
      const author = book.authors?.[0]?.name || "Unknown";
      const year = book.year || "Unknown";
      const title = book.bookName || "Untitled";
      return Promise.resolve(`/library/${author}/${year} - ${title}/${title}.m4b`);
    });

    renderWithProviders(
      <BookEditForm
        initialBook={initialBook}
        currentPath="/library/Jane Author/2020 - Original Title/Original Title.m4b"
        onSave={vi.fn()}
      />,
    );

    // Initial path matches currentPath so File Location / Target Path is not rendered
    await waitFor(() => expect(audiobookApi.generateNewPath).toHaveBeenCalled());
    expect(screen.queryByText(/File Location \/ Target Path/i)).not.toBeInTheDocument();

    // Changing title causes target path to differ and renders DiffDisplay
    const titleInput = screen.getByDisplayValue("Original Title");
    fireEvent.change(titleInput, { target: { value: "New Path" } });

    expect(
      await screen.findByText(/File Location \/ Target Path/i, undefined, { timeout: 3000 }),
    ).toBeInTheDocument();
  });

  it("shows an informational message when series is set but the series part is empty", () => {
    renderWithProviders(
      <BookEditForm
        initialBook={{ ...initialBook, series: "The Stormlight Archive" }}
        onSave={vi.fn()}
      />,
    );

    expect(screen.getByText(/Series is set but no series part is entered/i)).toBeInTheDocument();
  });

  it("flags series-part conflicts via normal anchor links to the other books", async () => {
    const { audiobookApi } = await import("@/services/api");
    vi.mocked(audiobookApi.getSeriesPartConflicts).mockResolvedValue({
      conflicts: [{ audiobookId: 99, bookName: "Words of Radiance", seriesPart: "2" }],
      truncated: false,
    });

    renderWithProviders(
      <BookEditForm
        initialBook={{ ...initialBook, series: "The Stormlight Archive", seriesPart: "1" }}
        onSave={vi.fn()}
        currentBookId={42}
      />,
    );

    await waitFor(() =>
      expect(audiobookApi.getSeriesPartConflicts).toHaveBeenCalledWith(
        42,
        "The Stormlight Archive",
        "1",
      ),
    );

    expect(
      await screen.findByText(/another book already uses this series part/i),
    ).toBeInTheDocument();

    const conflictLink = screen.getByRole("link", { name: /Words of Radiance/i });
    expect(conflictLink.getAttribute("href")).toBe("/library/book/99");
    expect(conflictLink.getAttribute("target")).toBe("_blank");
    expect(conflictLink.getAttribute("rel")).toBe("noopener noreferrer");

    // Advisory only - the save button stays enabled, which the requirement calls out.
    expect(screen.getByRole("button", { name: /save audiobook/i })).toBeEnabled();
  });

  it("tells the user when the conflict list is truncated, while saving stays allowed", async () => {
    const { audiobookApi } = await import("@/services/api");
    vi.mocked(audiobookApi.getSeriesPartConflicts).mockResolvedValue({
      conflicts: [{ audiobookId: 99, bookName: "Words of Radiance", seriesPart: "2" }],
      truncated: true,
    });

    renderWithProviders(
      <BookEditForm
        initialBook={{ ...initialBook, series: "The Stormlight Archive", seriesPart: "1" }}
        onSave={vi.fn()}
        currentBookId={42}
      />,
    );

    expect(
      await screen.findByText(/list is truncated — more books share this series part/i),
    ).toBeInTheDocument();
    expect(screen.getByText(/saving is still allowed/i)).toBeInTheDocument();
  });

  it("surfaces an advisory error instead of pretending no conflicts when the check fails", async () => {
    const { audiobookApi } = await import("@/services/api");
    vi.mocked(audiobookApi.getSeriesPartConflicts).mockRejectedValueOnce(new Error("network down"));

    renderWithProviders(
      <BookEditForm
        initialBook={{ ...initialBook, series: "The Stormlight Archive", seriesPart: "1" }}
        onSave={vi.fn()}
        currentBookId={42}
      />,
    );

    expect(
      await screen.findByText(/couldn't check for series-part conflicts/i),
    ).toBeInTheDocument();
    // The warning must not be presented as "no conflicts" - and saving stays allowed.
    expect(
      screen.queryByText(/another book already uses this series part/i),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /save audiobook/i })).toBeEnabled();
  });

  it("skips the conflict check when no currentBookId is provided (new-book flows)", async () => {
    renderWithProviders(
      <BookEditForm
        initialBook={{ ...initialBook, series: "The Stormlight Archive", seriesPart: "1" }}
        onSave={vi.fn()}
      />,
    );

    // The query is disabled without a currentBookId regardless of the debounce, so this is
    // deterministic - not a fixed-sleep race.
    const { audiobookApi } = await import("@/services/api");
    expect(audiobookApi.getSeriesPartConflicts).not.toHaveBeenCalled();
  });

  it("opens the search dialog on mount when autoOpenSearchDialog is set", async () => {
    renderWithProviders(
      <BookEditForm initialBook={initialBook} onSave={vi.fn()} autoOpenSearchDialog />,
    );

    expect(
      await screen.findByPlaceholderText("Search title, author, or paste URL..."),
    ).toBeInTheDocument();
  });

  it("does not open the search dialog on mount when autoOpenSearchDialog is unset", () => {
    renderWithProviders(<BookEditForm initialBook={initialBook} onSave={vi.fn()} />);

    expect(
      screen.queryByPlaceholderText("Search title, author, or paste URL..."),
    ).not.toBeInTheDocument();
  });

  it("calls onAutoSaveFromSearch right before the auto-submit when a search result auto-saves", async () => {
    const { metadataSearchApi } = await import("@/services/api");
    vi.mocked(metadataSearchApi.searchMultiple).mockResolvedValueOnce({
      results: [
        {
          url: "https://audible.com/pd/B09KDG66KL",
          cleanUrl: "https://audible.com/pd/B09KDG66KL",
          source: "Audible",
          bookName: "Scraped Book",
          authors: [{ name: "Jane Author" }],
          narrators: [],
          series: [],
          genres: [],
        },
      ],
      sourceStatuses: [],
    });

    const onAutoSaveFromSearch = vi.fn();
    const onSave = vi.fn<(book: Audiobook) => Promise<void>>().mockResolvedValue(undefined);
    renderWithProviders(
      <BookEditForm
        initialBook={initialBook}
        onSave={onSave}
        onAutoSaveFromSearch={onAutoSaveFromSearch}
      />,
    );

    fireEvent.click(screen.getByText("Search Online Metadata"));
    const searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Scraped" } });
    fireEvent.submit(searchInput.closest("form")!);
    const applyButton = await screen.findByRole("button", { name: "Apply" });
    fireEvent.click(applyButton);
    const applyAllButton = await screen.findByRole("button", { name: "Apply & Save All" });
    fireEvent.click(applyAllButton);

    expect(onAutoSaveFromSearch).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
  });

  it("does not call onAutoSaveFromSearch when the don't-save-automatically toggle is checked", async () => {
    const { metadataSearchApi } = await import("@/services/api");
    vi.mocked(metadataSearchApi.searchMultiple).mockResolvedValueOnce({
      results: [
        {
          url: "https://audible.com/pd/B09KDG66KL",
          cleanUrl: "https://audible.com/pd/B09KDG66KL",
          source: "Audible",
          bookName: "Scraped Book",
          authors: [{ name: "Jane Author" }],
          narrators: [],
          series: [],
          genres: [],
        },
      ],
      sourceStatuses: [],
    });

    const onAutoSaveFromSearch = vi.fn();
    const onSave = vi.fn<(book: Audiobook) => Promise<void>>().mockResolvedValue(undefined);
    renderWithProviders(
      <BookEditForm
        initialBook={initialBook}
        onSave={onSave}
        onAutoSaveFromSearch={onAutoSaveFromSearch}
      />,
    );

    fireEvent.click(screen.getByText("Search Online Metadata"));
    const searchInput = await screen.findByPlaceholderText("Search title, author, or paste URL...");
    fireEvent.change(searchInput, { target: { value: "Scraped" } });
    fireEvent.submit(searchInput.closest("form")!);
    const applyButton = await screen.findByRole("button", { name: "Apply" });
    fireEvent.click(applyButton);
    fireEvent.click(screen.getByRole("checkbox", { name: "Don't save automatically" }));
    const applyAllButton = await screen.findByRole("button", { name: "Apply All" });
    fireEvent.click(applyAllButton);

    expect(await screen.findByDisplayValue("Scraped Book")).toBeInTheDocument();
    expect(onAutoSaveFromSearch).not.toHaveBeenCalled();
    expect(onSave).not.toHaveBeenCalled();
  });
});
