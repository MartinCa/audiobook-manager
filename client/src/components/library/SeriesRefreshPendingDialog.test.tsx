import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SignalRContext } from "@/context/SignalRContext";
import { seriesApi } from "@/services/api";
import { SeriesRefreshPendingDialog } from "./SeriesRefreshPendingDialog";
import type { SeriesRefreshPending } from "@/types/SeriesRefresh";

import type * as ApiModule from "@/services/api";
import type * as SonnerModule from "sonner";

vi.mock("sonner", async (importOriginal) => {
  const actual = await importOriginal<typeof SonnerModule>();
  return {
    ...actual,
    toast: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
  };
});

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    seriesApi: {
      getSeriesPending: vi.fn(),
      getMissingBookCandidates: vi.fn(),
      applySeriesPending: vi.fn().mockResolvedValue(undefined),
      dismissSeriesPending: vi.fn().mockResolvedValue(undefined),
    },
    operationsApi: {
      getStatus: vi.fn().mockResolvedValue({ isRunning: false }),
    },
  };
});

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

function pendingFixture(): SeriesRefreshPending {
  return {
    seriesName: "Mistborn",
    sourceName: "Hardcover",
    sourceUrl: "https://hardcover.app/series/42",
    sourceSeriesName: "Mistborn Saga",
    fetchedAt: "2026-09-01T12:00:00Z",
    changes: [
      {
        changeType: "PartUpdate",
        audiobookId: 5,
        bookName: "Book A",
        storedPart: "01",
        newPart: "02",
        rosterTitle: "Book A",
        position: null,
        title: null,
        year: null,
      },
      {
        changeType: "MissingBook",
        audiobookId: null,
        bookName: "Book B",
        storedPart: null,
        newPart: null,
        rosterTitle: null,
        position: "4",
        title: "Book B",
        year: 2010,
      },
      {
        changeType: "PartRemoval",
        audiobookId: 7,
        bookName: "Book C",
        storedPart: "3",
        newPart: null,
        rosterTitle: "Book C",
        position: null,
        title: null,
        year: null,
      },
    ],
  };
}

function renderDialog(open = true) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <SignalRContext.Provider value={mockSignalRValue}>
      <QueryClientProvider client={queryClient}>
        <SeriesRefreshPendingDialog
          open={open}
          onOpenChange={() => {}}
          seriesName="Mistborn"
          onApplied={() => {}}
        />
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
}

describe("SeriesRefreshPendingDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(seriesApi.getSeriesPending).mockResolvedValue(pendingFixture());
  });

  it("renders every change kind and the source-name adoption option", async () => {
    renderDialog();

    expect(await screen.findByText(/Review Series Refresh/)).toBeInTheDocument();
    expect(await screen.findByText(/Book A · part 01 → 02/)).toBeInTheDocument();
    expect(screen.getByText(/Book B/)).toBeInTheDocument();
    expect(screen.getByText(/Book C · part 3 → no part/)).toBeInTheDocument();
    expect(screen.getByText(/"Mistborn" → "Mistborn Saga"/)).toBeInTheDocument();
  });

  it("sends only the accepted selections when applying", async () => {
    vi.mocked(seriesApi.getMissingBookCandidates).mockResolvedValue([
      {
        audiobookId: 9,
        bookName: "Candidate Book",
        year: 2010,
        authors: ["Brandon Sanderson"],
        series: "Mistborn",
        seriesPart: "4",
        titleSimilarity: 0.95,
        authorMatches: true,
      },
    ]);

    renderDialog();
    await screen.findByText(/Book A · part 01 → 02/);

    // The part update and part removal rows start selected; uncheck the removal.
    fireEvent.click(screen.getByRole("checkbox", { name: /Book C · part 3 → no part/ }));

    // A missing book needs a chosen library book; open its picker and choose the top candidate.
    // Choosing a candidate arms the missing-book change in the same click.
    fireEvent.click(screen.getByRole("button", { name: /Find in library/ }));
    await waitFor(() => {
      expect(seriesApi.getMissingBookCandidates).toHaveBeenCalledWith("Mistborn", "4", "Book B");
    });
    const candidate = await screen.findByRole("button", { name: /Candidate Book/ });
    fireEvent.click(candidate);

    fireEvent.click(screen.getByRole("checkbox", { name: /Adopt source series name/ }));
    fireEvent.click(screen.getByRole("button", { name: /Apply 2/ }));

    await waitFor(() => {
      expect(seriesApi.applySeriesPending).toHaveBeenCalledWith("Mistborn", {
        adoptSourceSeriesName: true,
        selections: [
          { changeType: "PartUpdate", audiobookId: 5 },
          { changeType: "MissingBook", audiobookId: 9, position: "4", title: "Book B" },
        ],
      });
    });
  });

  it("keeps a missing book disabled until a library book is chosen", async () => {
    renderDialog();
    await screen.findByText(/Book B/);

    // The two part changes start selected; the unarmed missing book stays disabled until a
    // library book is chosen for it - the applied part changes never include an unchosen book.
    expect(
      await screen.findByRole("checkbox", { name: /Apply missing book "Book B"/ }),
    ).toHaveAttribute("aria-disabled", "true");
    expect(screen.getByRole("button", { name: /Apply 2/ })).toBeEnabled();
  });

  it("defaults part changes to selected", async () => {
    renderDialog();
    await screen.findByText(/Book A · part 01 → 02/);

    expect(screen.getByRole("checkbox", { name: /Book A · part 01 → 02/ })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: /Book C · part 3 → no part/ })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: /Adopt source series name/ })).not.toBeChecked();
  });

  it("dismisses through the pending endpoint", async () => {
    renderDialog();
    await screen.findByText(/Book A · part 01 → 02/);

    fireEvent.click(screen.getByRole("button", { name: /Dismiss/ }));

    await waitFor(() => {
      expect(seriesApi.dismissSeriesPending).toHaveBeenCalledWith("Mistborn");
    });
  });

  it("does not report a vanished apply as a success", async () => {
    const { toast } = await import("sonner");
    renderDialog();
    await screen.findByText(/Book A · part 01 → 02/);

    // Arm `applying` first: the completion handler only reacts while applying.
    fireEvent.click(screen.getByRole("button", { name: /Apply 2/ }));

    const completeCall = (mockSignalRValue as { on: ReturnType<typeof vi.fn> }).on.mock.calls.find(
      (call) => call[0] === "SeriesRefreshApplyComplete",
    );
    const completeHandler = completeCall?.[1] as (data: {
      totalProcessed: number;
      totalSucceeded: number;
      totalFailed: number;
    }) => void;
    expect(completeHandler).toBeDefined();

    // A completion with totalProcessed === 0 means the pending row was gone before the apply
    // ran (dismissed elsewhere, or superseded): an info notice, never a "Applied N" success.
    completeHandler({ totalProcessed: 0, totalSucceeded: 0, totalFailed: 0 });

    await waitFor(() => {
      expect(toast.info).toHaveBeenCalledWith(
        "The pending changes were already gone - nothing was applied",
      );
    });
  });
});
