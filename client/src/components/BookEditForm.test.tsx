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
});
