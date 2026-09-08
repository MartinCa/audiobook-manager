import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MissingBookCandidatesDialog } from "./MissingBookCandidatesDialog";
import { seriesApi } from "@/services/api";
import type { SeriesBookCandidate } from "@/types/Series";

vi.mock("@/services/api", () => ({
  seriesApi: {
    getMissingBookCandidates: vi.fn(),
    applyMissingBook: vi.fn(),
  },
  handleApiError: vi.fn((err: unknown) => ({ message: String(err) })),
}));

vi.mock("sonner", () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const candidateA: SeriesBookCandidate = {
  audiobookId: 10,
  bookName: "Candlekeep Chronicles",
  year: 2013,
  authors: ["R. A. Salvatore"],
  series: "Legacy of the Drow",
  seriesPart: "3",
  titleSimilarity: 0.95,
  authorMatches: true,
};

const candidateB: SeriesBookCandidate = {
  audiobookId: 20,
  bookName: "Candlekeep Chronicles - Other",
  year: 2014,
  authors: ["R. A. Salvatore"],
  series: null,
  seriesPart: null,
  titleSimilarity: 0.7,
  authorMatches: false,
};

const candidateC: SeriesBookCandidate = {
  audiobookId: 30,
  bookName: "The Sunspire",
  year: 2015,
  authors: ["R. A. Salvatore"],
  series: null,
  seriesPart: null,
  titleSimilarity: 0.9,
  authorMatches: true,
};

const missingBook = { id: 99, position: "4", title: "Candlekeep Chronicles" };

function renderModal() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MissingBookCandidatesDialog
        open
        onOpenChange={vi.fn()}
        seriesName="The Legend of Drizzt"
        missingBook={missingBook}
      />
    </QueryClientProvider>,
  );
}

describe("MissingBookCandidatesDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows candidates in the server's ranked order when data loads", async () => {
    // The client no longer re-sorts: it trusts the backend's ranking and renders the response
    // unchanged, so feed the server-ranked order ([candidateA] first) and assert the rendered
    // order preserves it. A client-side re-sort used to run here and could silently reorder
    // ties differently than the server's more complete tie-breaker chain.
    vi.mocked(seriesApi.getMissingBookCandidates).mockResolvedValue([candidateA, candidateB]);

    renderModal();

    // candidateA is ranked first by the server, so its card appears before candidateB's.
    const titleA = await screen.findByText("Candlekeep Chronicles");
    const titleB = screen.getByText("Candlekeep Chronicles - Other");
    expect(titleA.compareDocumentPosition(titleB) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0);
    expect(screen.getByText("Author + title match")).toBeInTheDocument();
    expect(screen.getByText("Title match")).toBeInTheDocument();
    expect(screen.getByText("2 candidates found, ranked by match quality.")).toBeInTheDocument();
  });

  it("shows empty state when no candidates are returned", async () => {
    vi.mocked(seriesApi.getMissingBookCandidates).mockResolvedValue([]);

    renderModal();

    expect(
      await screen.findByText("No candidates found in the library for this missing book."),
    ).toBeInTheDocument();
  });

  it("Select button transitions to confirmation without calling applyMissingBook", async () => {
    vi.mocked(seriesApi.getMissingBookCandidates).mockResolvedValue([candidateA, candidateB]);

    renderModal();

    await screen.findByText("Candlekeep Chronicles");

    // Two candidates have two Select buttons, target the first one
    const selectButtons = screen.getAllByRole("button", { name: "Select" });
    fireEvent.click(selectButtons[0]!);

    // Confirm the confirmation UI appeared
    expect(
      screen.getByText("Confirm to apply the following book to the missing slot:"),
    ).toBeInTheDocument();
    expect(screen.getByText("Back")).toBeInTheDocument();
    expect(screen.getByText("Confirm")).toBeInTheDocument();

    // applyMissingBook should NOT have been called yet
    expect(seriesApi.applyMissingBook).not.toHaveBeenCalled();
  });

  it("Confirm button calls applyMissingBook with exact selected candidate args", async () => {
    vi.mocked(seriesApi.getMissingBookCandidates).mockResolvedValue([candidateA, candidateB]);
    vi.mocked(seriesApi.applyMissingBook).mockResolvedValue(undefined);

    renderModal();

    await screen.findByText("Candlekeep Chronicles");

    // Click Select on first candidate
    fireEvent.click(screen.getAllByRole("button", { name: "Select" })[0]!);
    expect(screen.getByText("Confirm")).toBeInTheDocument();

    // Click Confirm
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => {
      expect(seriesApi.applyMissingBook).toHaveBeenCalledWith(
        "The Legend of Drizzt",
        candidateA.audiobookId,
        missingBook.position,
        missingBook.title,
      );
    });
  });

  it("Back button during confirmation resets state but keeps dialog open", async () => {
    const onOpenChange = vi.fn();
    vi.mocked(seriesApi.getMissingBookCandidates).mockResolvedValue([candidateA]);

    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    render(
      <QueryClientProvider client={qc}>
        <MissingBookCandidatesDialog
          open
          onOpenChange={onOpenChange}
          seriesName="Test Series"
          missingBook={missingBook}
        />
      </QueryClientProvider>,
    );

    await screen.findByText("Candlekeep Chronicles");
    fireEvent.click(screen.getByRole("button", { name: /select/i }));
    expect(screen.getByText("Back")).toBeInTheDocument();

    // Back button resets confirmation state but does NOT close the dialog
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(onOpenChange).not.toHaveBeenCalled();
    expect(seriesApi.applyMissingBook).not.toHaveBeenCalled();
    // Candidate list should be visible again
    expect(screen.getByText("Candlekeep Chronicles")).toBeInTheDocument();
  });

  it("Cancel button closes the dialog without calling API", async () => {
    const onOpenChange = vi.fn();
    vi.mocked(seriesApi.getMissingBookCandidates).mockResolvedValue([candidateA]);

    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    render(
      <QueryClientProvider client={qc}>
        <MissingBookCandidatesDialog
          open
          onOpenChange={onOpenChange}
          seriesName="Test Series"
          missingBook={missingBook}
        />
      </QueryClientProvider>,
    );

    await screen.findByText("Candlekeep Chronicles");
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(seriesApi.applyMissingBook).not.toHaveBeenCalled();
  });

  it("shows error state with Retry button when query fails", async () => {
    vi.mocked(seriesApi.getMissingBookCandidates).mockRejectedValue(new Error("fetch failed"));

    renderModal();

    await waitFor(() => {
      expect(screen.getByText("Failed to load candidates.")).toBeInTheDocument();
    });
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
  });

  it("confirmation panel displays the target slot", async () => {
    vi.mocked(seriesApi.getMissingBookCandidates).mockResolvedValue([candidateA]);

    renderModal();

    await screen.findByText("Candlekeep Chronicles");
    fireEvent.click(screen.getAllByRole("button", { name: "Select" })[0]!);

    expect(screen.getByText('Position 4 — "Candlekeep Chronicles"')).toBeInTheDocument();
  });

  it("gracefully handles applyMissingBook failure and keeps dialog open", async () => {
    vi.mocked(seriesApi.getMissingBookCandidates).mockResolvedValue([candidateA]);
    vi.mocked(seriesApi.applyMissingBook).mockRejectedValue(new Error("network error"));

    renderModal();

    await screen.findByText("Candlekeep Chronicles");
    fireEvent.click(screen.getByRole("button", { name: /select/i }));
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(seriesApi.applyMissingBook).toHaveBeenCalled());
    // Dialog should still be open after failure
    expect(screen.getByText("Candidates for Missing Book")).toBeInTheDocument();
  });

  // Regression: closing and reopening the dialog for the SAME missing book used to keep the
  // confirming flag and selected candidate alive (the component stays mounted; only the portal
  // unmounts), so the reopen showed the previous confirmation panel instead of the candidate
  // list. Fails against the pre-fix dialog, which never reset the selection on close.
  it("closing then reopening shows the candidate list, not a stale confirmation panel", async () => {
    vi.mocked(seriesApi.getMissingBookCandidates).mockResolvedValue([candidateA, candidateB]);

    const holder = { open: true };
    const onOpenChange = (next: boolean) => {
      holder.open = next;
    };
    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const { rerender } = render(
      <QueryClientProvider client={qc}>
        <MissingBookCandidatesDialog
          open={holder.open}
          onOpenChange={onOpenChange}
          seriesName="The Legend of Drizzt"
          missingBook={missingBook}
        />
      </QueryClientProvider>,
    );
    const rerenderDialog = () =>
      rerender(
        <QueryClientProvider client={qc}>
          <MissingBookCandidatesDialog
            open={holder.open}
            onOpenChange={onOpenChange}
            seriesName="The Legend of Drizzt"
            missingBook={missingBook}
          />
        </QueryClientProvider>,
      );

    await screen.findByText("Candlekeep Chronicles");
    fireEvent.click(screen.getAllByRole("button", { name: /select/i })[0]!);
    expect(
      screen.getByText("Confirm to apply the following book to the missing slot:"),
    ).toBeInTheDocument();

    // Close (X / Esc / overlay all funnel through the same onOpenChange callback here).
    onOpenChange(false);
    rerenderDialog();

    // Reopen for the same book: the list must render, never the stale confirmation panel.
    onOpenChange(true);
    rerenderDialog();

    expect(screen.getByText("2 candidates found, ranked by match quality.")).toBeInTheDocument();
    expect(
      screen.queryByText("Confirm to apply the following book to the missing slot:"),
    ).not.toBeInTheDocument();
    expect(seriesApi.applyMissingBook).not.toHaveBeenCalled();
  });

  // Regression: a selection made for one missing book used to survive into another book's
  // dialog (the component stays mounted across onOpenChange), so switching target books could
  // show a stale confirmation panel and apply the PREVIOUS book's candidate to the wrong slot.
  // Fails against the pre-fix dialog, which never reset the selection on identity change.
  it("reopening for a different missing book shows its own list and no stale selection", async () => {
    vi.mocked(seriesApi.getMissingBookCandidates).mockImplementation(
      (_series, _position, title) => {
        // Different candidate per target book, so the test can tell WHICH book's data rendered.
        return Promise.resolve(title === "Candlekeep Chronicles" ? [candidateA] : [candidateC]);
      },
    );

    const holder = { open: true, position: "4", title: "Candlekeep Chronicles" };
    const onOpenChange = (next: boolean) => {
      holder.open = next;
    };
    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const { rerender } = render(
      <QueryClientProvider client={qc}>
        <MissingBookCandidatesDialog
          open={holder.open}
          onOpenChange={onOpenChange}
          seriesName="The Legend of Drizzt"
          missingBook={missingBook}
        />
      </QueryClientProvider>,
    );
    const rerenderDialog = () =>
      rerender(
        <QueryClientProvider client={qc}>
          <MissingBookCandidatesDialog
            open={holder.open}
            onOpenChange={onOpenChange}
            seriesName="The Legend of Drizzt"
            missingBook={{ id: 100, position: holder.position, title: holder.title }}
          />
        </QueryClientProvider>,
      );

    await screen.findByText("Candlekeep Chronicles");
    fireEvent.click(screen.getByRole("button", { name: /select/i }));
    expect(
      screen.getByText("Confirm to apply the following book to the missing slot:"),
    ).toBeInTheDocument();

    // Parent reopens the dialog for a DIFFERENT missing book of the same series.
    onOpenChange(false);
    rerenderDialog();
    holder.position = "5";
    holder.title = "The Sunspire";
    onOpenChange(true);
    rerenderDialog();

    // The new book's candidate list is shown...
    expect(await screen.findByText("The Sunspire")).toBeInTheDocument();
    // ...and neither the stale confirmation panel nor the old book's candidate survives.
    expect(
      screen.queryByText("Confirm to apply the following book to the missing slot:"),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("Candlekeep Chronicles")).not.toBeInTheDocument();
    expect(seriesApi.applyMissingBook).not.toHaveBeenCalled();
  });
});
