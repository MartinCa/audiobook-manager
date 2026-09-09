import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BulkBookEditDialog } from "./BulkBookEditDialog";
import type * as SonnerModule from "sonner";
import { toast } from "sonner";
import type * as ApiModule from "@/services/api";
import { bulkEditApi } from "@/services/api";
import type { SelectedBookInfo } from "@/hooks/useBookSelection";
import type { BulkEditPreviewBook } from "@/types/BulkEdit";

vi.mock("sonner", async (importOriginal) => {
  const actual = await importOriginal<typeof SonnerModule>();
  return {
    ...actual,
    toast: {
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
      warning: vi.fn(),
    },
  };
});

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    bulkEditApi: {
      preview: vi.fn(),
      apply: vi.fn().mockResolvedValue(undefined),
    },
    similarValuesApi: {
      getAuthorNames: vi.fn().mockResolvedValue([]),
      getNarratorNames: vi.fn().mockResolvedValue([]),
      getSeriesNames: vi.fn().mockResolvedValue([]),
    },
    settingsApi: {
      getLanguages: vi.fn().mockResolvedValue({ languages: [] }),
    },
  };
});

let queryClient: QueryClient;
const onOpenChange = vi.fn();

function book(overrides: Partial<BulkEditPreviewBook> = {}): BulkEditPreviewBook {
  return {
    id: 1,
    authors: [],
    narrators: [],
    genres: [],
    bookName: null,
    subtitle: null,
    series: null,
    seriesPart: null,
    year: null,
    description: null,
    copyright: null,
    publisher: null,
    language: null,
    rating: null,
    asin: null,
    www: null,
    ...overrides,
  };
}

function identicalBooks(): BulkEditPreviewBook[] {
  return [
    book({
      id: 1,
      bookName: "The Way of Kings",
      subtitle: "Book One",
      authors: ["Brandon Sanderson"],
      narrators: ["Michael Kramer"],
      genres: ["Fantasy"],
      series: "Stormlight Archive",
      seriesPart: "1",
      year: 2010,
      description: "A stormy epic.",
      language: "en",
      publisher: "Tor",
      copyright: "2010 Tor Books",
      rating: "4.8",
      asin: "B123445",
      www: "https://example.com/wok",
    }),
    book({
      id: 2,
      bookName: "The Way of Kings",
      subtitle: "Book One",
      authors: ["Brandon Sanderson"],
      narrators: ["Michael Kramer"],
      genres: ["Fantasy"],
      series: "Stormlight Archive",
      seriesPart: "1",
      year: 2010,
      description: "A stormy epic.",
      language: "en",
      publisher: "Tor",
      copyright: "2010 Tor Books",
      rating: "4.8",
      asin: "B123445",
      www: "https://example.com/wok",
    }),
  ];
}

const selectedBooks: SelectedBookInfo[] = [
  { id: 1, title: "The Way of Kings", authors: ["Brandon Sanderson"] },
  { id: 2, title: "The Way of Kings", authors: ["Brandon Sanderson"] },
];

function renderDialog(previewBooks: BulkEditPreviewBook[], open = true) {
  vi.mocked(bulkEditApi.preview).mockResolvedValue({ books: previewBooks });

  render(
    <QueryClientProvider client={queryClient}>
      <BulkBookEditDialog open={open} onOpenChange={onOpenChange} selectedBooks={selectedBooks} />
    </QueryClientProvider>,
  );
}

async function submit() {
  fireEvent.click(await screen.findByRole("button", { name: "Apply" }));
}

describe("BulkBookEditDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
  });

  it("prefills a single-value field with the common value across the selected books", async () => {
    renderDialog(identicalBooks());

    expect(await screen.findByDisplayValue("The Way of Kings")).toBeInTheDocument();
    expect(await screen.findByDisplayValue("Tor")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Stormlight Archive")).toBeInTheDocument();
    expect(screen.getByDisplayValue("2010")).toBeInTheDocument();
  });

  it("prefills multi-value fields only when the books agree exactly", async () => {
    renderDialog(identicalBooks());

    expect(
      await screen.findByText("Brandon Sanderson", { selector: "button" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Michael Kramer", { selector: "button" })).toBeInTheDocument();
    expect(screen.getByText("Fantasy", { selector: "button" })).toBeInTheDocument();
  });

  // Regression for the review finding: FieldRow renders a label around the four shared field
  // components, which render their own labels - so Authors/Narrators/Series/Language each
  // showed twice. The components' showLabel={false} suppresses the inner label here, letting
  // FieldRow's label win and keeping BookEditForm's DOM untouched.
  it("renders each shared field's label exactly once (FieldRow's label wins)", async () => {
    renderDialog(identicalBooks());
    await screen.findByDisplayValue("The Way of Kings");

    expect(screen.getAllByText(/Authors/)).toHaveLength(1);
    expect(screen.getAllByText("Narrators")).toHaveLength(1);
    expect(screen.getAllByText("Series")).toHaveLength(1);
    expect(screen.getAllByText("Language")).toHaveLength(1);
  });

  it("shows an empty input with the 'Different values' placeholder when the books disagree", async () => {
    renderDialog([
      book({ id: 1, bookName: "The Way of Kings" }),
      book({ id: 2, bookName: "Words of Radiance" }),
    ]);

    const titleInput = await screen.findByPlaceholderText("Different values");
    expect(titleInput).toHaveValue("");
  });

  it("shows the mixed placeholder on a multi-value field too", async () => {
    renderDialog([
      book({ id: 1, authors: ["Brandon Sanderson"], narrators: [] }),
      book({ id: 2, authors: ["Isaac Stewart"], narrators: [] }),
    ]);

    const authorsInput = await screen.findByLabelText("Different values");
    expect(authorsInput).toHaveValue("");
  });

  it("lists the affected books and the re-tag warning under the header", async () => {
    renderDialog(identicalBooks());

    expect(await screen.findByText("Edit Metadata for 2 Books")).toBeInTheDocument();
    expect(await screen.findByText("Affected books")).toBeInTheDocument();
    expect(screen.getAllByText("Brandon Sanderson — The Way of Kings")).toHaveLength(2);
    expect(screen.getByText(/Each book's m4b file will be re-tagged/)).toBeInTheDocument();
  });

  it("keeps Apply disabled with the empty-fields hint until a field actually changes", async () => {
    renderDialog(identicalBooks());
    await screen.findByDisplayValue("The Way of Kings");

    const apply = screen.getByRole("button", { name: "Apply" });
    expect(apply).toBeDisabled();
    expect(
      screen.getByText("Enter a value or mark fields to clear — empty fields are left unchanged."),
    ).toBeInTheDocument();

    // Typing the field's own prefilled value changes nothing about the payload's shape, but it
    // does count as a deliberate edit.
    fireEvent.click(apply);
    expect(bulkEditApi.apply).not.toHaveBeenCalled();
  });

  it("sends a single-field set payload with the typed value and nothing else", async () => {
    renderDialog(identicalBooks());
    const titleInput = await screen.findByDisplayValue("The Way of Kings");

    const user = userEvent.setup();
    await user.clear(titleInput);
    await user.type(titleInput, "Words of Radiance");
    await submit();

    await waitFor(() => {
      expect(bulkEditApi.apply).toHaveBeenCalledWith([1, 2], {
        bookName: { action: "set", value: "Words of Radiance" },
        audiobookIds: [1, 2],
      });
    });
    expect(screen.getByText("1 field will be set across 2 books")).toBeInTheDocument();
    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Bulk edit started for 2 books");
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("sends a year set payload as a number", async () => {
    renderDialog(identicalBooks());
    const yearInput = await screen.findByDisplayValue("2010");

    fireEvent.change(yearInput, { target: { value: "2020" } });
    await submit();

    await waitFor(() => {
      expect(bulkEditApi.apply).toHaveBeenCalledWith([1, 2], {
        year: { action: "set", value: 2020 },
        audiobookIds: [1, 2],
      });
    });
  });

  it("marks a field clear through its checkbox, disables the input and sends the clear action", async () => {
    renderDialog(identicalBooks());
    await screen.findByDisplayValue("Stormlight Archive");

    fireEvent.click(screen.getByRole("checkbox", { name: "Clear Series" }));

    expect(screen.getByText("Will be cleared on all books")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Stormlight Archive")).toBeDisabled();

    await submit();

    await waitFor(() => {
      expect(bulkEditApi.apply).toHaveBeenCalledWith([1, 2], {
        series: { action: "clear" },
        audiobookIds: [1, 2],
      });
    });
    expect(screen.getByText(/0 fields will be set · 1 cleared across 2 books/)).toBeInTheDocument();
  });

  it("sends a multi replace payload with the reordered list", async () => {
    renderDialog(identicalBooks());
    await screen.findByDisplayValue("The Way of Kings");
    const user = userEvent.setup();
    const input = screen.getByLabelText("Author Name, Second Author");
    await user.type(input, "Isaac Stewart{enter}");

    await submit();

    await waitFor(() => {
      expect(bulkEditApi.apply).toHaveBeenCalledWith([1, 2], {
        authors: {
          action: "replace",
          values: ["Brandon Sanderson", "Isaac Stewart"],
        },
        audiobookIds: [1, 2],
      });
    });
  });

  it("sends an add payload when the radio says to keep existing values", async () => {
    renderDialog(identicalBooks());
    await screen.findByDisplayValue("The Way of Kings");
    const addRadios = await screen.findAllByRole("radio", { name: "Add to existing values" });
    // Three multi fields each render a radio; the authors group is the first in field order.
    fireEvent.click(addRadios[0]!);
    const user = userEvent.setup();
    const input = screen.getByLabelText("Author Name, Second Author");
    await user.type(input, "Isaac Stewart{enter}");

    await submit();

    await waitFor(() => {
      expect(bulkEditApi.apply).toHaveBeenCalledWith([1, 2], {
        authors: {
          action: "add",
          values: ["Brandon Sanderson", "Isaac Stewart"],
        },
        audiobookIds: [1, 2],
      });
    });
  });

  it("renders a clear control on every non-critical field, narrators and genres included", async () => {
    renderDialog(identicalBooks());
    await screen.findByDisplayValue("The Way of Kings");

    // The critical fields can only be set: Authors must never empty, and the backend refuses a
    // clear on Book Title and Year - none of them renders a Clear checkbox.
    for (const name of ["Clear Authors", "Clear Book Title", "Clear Year"]) {
      expect(screen.queryByRole("checkbox", { name })).not.toBeInTheDocument();
    }

    // Every other field - the clearable single fields plus the two multi fields whose emptiness
    // is legitimate - does get one.
    for (const name of [
      "Clear Series",
      "Clear Narrators",
      "Clear Genres",
      "Clear Description",
      "Clear Language",
    ]) {
      expect(screen.getByRole("checkbox", { name })).toBeInTheDocument();
    }
  });

  it("sends a narrators clear payload, hiding the mode radio and disabling the field", async () => {
    renderDialog(identicalBooks());
    await screen.findByDisplayValue("The Way of Kings");

    fireEvent.click(screen.getByRole("checkbox", { name: "Clear Narrators" }));

    expect(screen.getByText("Will be cleared on all books")).toBeInTheDocument();
    expect(screen.getByLabelText("Narrator Name")).toBeDisabled();
    // The Replace/Add mode radio is mutually exclusive with Clear and hides while it is checked;
    // the other multi fields' radios stay.
    expect(document.getElementById("narrators-replace")).not.toBeInTheDocument();
    expect(document.getElementById("narrators-add")).not.toBeInTheDocument();
    expect(document.getElementById("genres-add")).toBeInTheDocument();

    await submit();

    await waitFor(() => {
      expect(bulkEditApi.apply).toHaveBeenCalledWith([1, 2], {
        narrators: { action: "clear", values: [] },
        audiobookIds: [1, 2],
      });
    });
    expect(screen.getByText(/0 fields will be set · 1 cleared across 2 books/)).toBeInTheDocument();
  });

  it("sends a genres set alongside a narrators clear and nothing else", async () => {
    renderDialog(identicalBooks());
    await screen.findByDisplayValue("The Way of Kings");

    const user = userEvent.setup();
    fireEvent.click(screen.getByRole("checkbox", { name: "Clear Narrators" }));
    const genresInput = screen.getByLabelText("Fantasy, Fiction");
    await user.type(genresInput, "Sci-Fi{enter}");

    await submit();

    // Clearing narrators is one explicit action; setting genres is another. The prefilled Book
    // Title, Series, etc. stay out of the payload - only the touched fields are sent.
    await waitFor(() => {
      expect(bulkEditApi.apply).toHaveBeenCalledWith([1, 2], {
        narrators: { action: "clear", values: [] },
        genres: { action: "replace", values: ["Fantasy", "Sci-Fi"] },
        audiobookIds: [1, 2],
      });
    });
    expect(screen.getByText(/1 field will be set · 1 cleared across 2 books/)).toBeInTheDocument();
  });

  it("shows a year-validation error on a non-year and never calls apply", async () => {
    renderDialog(identicalBooks());
    const yearInput = await screen.findByDisplayValue("2010");

    // A year of zero is an invalid date that the number input's own constraint validation still
    // lets through (it is step-aligned), so the submit reaches the zod refine. A value like
    // "2001.5" would be blocked earlier by the browser's native stepMismatch tooltip instead.
    fireEvent.change(yearInput, { target: { value: "0" } });
    await submit();

    expect(await screen.findByText("Year must be a positive whole number")).toBeInTheDocument();
    expect(bulkEditApi.apply).not.toHaveBeenCalled();
  });

  it("keeps the dialog open and toasts the error when apply is refused", async () => {
    vi.mocked(bulkEditApi.apply).mockRejectedValueOnce(new Error("Nothing to apply."));
    renderDialog(identicalBooks());
    await screen.findByDisplayValue("The Way of Kings");

    const user = userEvent.setup();
    const input = screen.getByLabelText("Author Name, Second Author");
    await user.type(input, "Isaac Stewart{enter}");
    await submit();

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith("Nothing to apply.");
    });
    expect(onOpenChange).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Apply" })).toBeInTheDocument();
  });

  it("disables Apply while the request is in flight", async () => {
    let release!: () => void;
    vi.mocked(bulkEditApi.apply).mockReturnValue(
      new Promise<void>((resolve) => {
        release = resolve;
      }),
    );
    renderDialog(identicalBooks());
    await screen.findByDisplayValue("The Way of Kings");

    const user = userEvent.setup();
    const input = screen.getByLabelText("Author Name, Second Author");
    await user.type(input, "Isaac Stewart{enter}");
    await submit();

    const apply = await screen.findByRole("button", { name: "Applying..." });
    expect(apply).toBeDisabled();

    release();
    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it("shows skeleton rows while the preview loads", () => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    vi.mocked(bulkEditApi.preview).mockImplementation(() => new Promise(() => {}));

    render(
      <QueryClientProvider client={queryClient}>
        <BulkBookEditDialog open onOpenChange={onOpenChange} selectedBooks={selectedBooks} />
      </QueryClientProvider>,
    );

    expect(screen.getByRole("status", { name: "Loading bulk edit preview" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Apply" })).not.toBeInTheDocument();
  });

  it("shows the preview error with a Retry button and recovers", async () => {
    vi.mocked(bulkEditApi.preview).mockRejectedValueOnce(new Error("Upstream is down."));
    renderDialog(identicalBooks());

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Couldn't load the preview");
    expect(alert).toHaveTextContent("Upstream is down.");

    fireEvent.click(screen.getByRole("button", { name: "Retry" }));

    expect(await screen.findByDisplayValue("The Way of Kings")).toBeInTheDocument();
  });
});
