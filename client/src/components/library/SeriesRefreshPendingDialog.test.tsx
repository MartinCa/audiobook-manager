import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SignalRContext } from "@/context/SignalRContext";
import { seriesApi } from "@/services/api";
import { SeriesRefreshPendingDialog } from "./SeriesRefreshPendingDialog";
import type { SeriesRefreshPending } from "@/types/SeriesRefresh";

import type * as ApiModule from "@/services/api";
import { notifications } from "@/lib/notifications";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    seriesApi: {
      getSeriesPending: vi.fn(),
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

function renderDialog(open = true, onApplied?: (renamedTo?: string | null) => void) {
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
          onApplied={onApplied ?? (() => {})}
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
    expect(screen.getByText(/Book C · part 3 → no part/)).toBeInTheDocument();
    expect(screen.getByText(/"Mistborn" → "Mistborn Saga"/)).toBeInTheDocument();
    // The backend no longer emits a MissingBook change (see AudiobookManager/
    // UPCOMING_RELEASES_DESIGN.md) - the section that used to review it is gone.
    expect(screen.queryByText(/Missing Source Books/)).not.toBeInTheDocument();
  });

  it("sends only the accepted selections when applying", async () => {
    renderDialog();
    await screen.findByText(/Book A · part 01 → 02/);

    // The part update and part removal rows start selected; uncheck the removal.
    fireEvent.click(screen.getByRole("checkbox", { name: /Book C · part 3 → no part/ }));

    fireEvent.click(screen.getByRole("checkbox", { name: /Adopt source series name/ }));
    fireEvent.click(screen.getByRole("button", { name: /Apply 1 \+ rename/ }));

    await waitFor(() => {
      expect(seriesApi.applySeriesPending).toHaveBeenCalledWith("Mistborn", {
        adoptSourceSeriesName: true,
        selections: [{ changeType: "PartUpdate", audiobookId: 5 }],
      });
    });
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

  it("still allows dismissing when the pending detail fetch fails", async () => {
    // The stored pending payload can't be parsed (e.g. after a downgrade or corruption), so the
    // detail call fails - but dismiss only needs the series name, not the payload, and must stay
    // available so the stuck pending row can still be cleared from the UI.
    vi.mocked(seriesApi.getSeriesPending).mockRejectedValue(new Error("boom"));

    renderDialog();
    await screen.findByText(/Failed to load pending changes/);

    expect(screen.getByRole("button", { name: /Dismiss/ })).toBeEnabled();
    expect(screen.getByRole("button", { name: /Apply/ })).toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: /Dismiss/ }));

    await waitFor(() => {
      expect(seriesApi.dismissSeriesPending).toHaveBeenCalledWith("Mistborn");
    });
  });

  it("allows an adoption-only apply with no individual changes selected", async () => {
    renderDialog();
    await screen.findByText(/Book A · part 01 → 02/);

    // Uncheck every individual change (the two part changes start selected) and keep only the
    // source-series-name adoption, mirroring the backend's supported adoption-only request.
    fireEvent.click(screen.getByRole("checkbox", { name: /Book A · part 01 → 02/ }));
    fireEvent.click(screen.getByRole("checkbox", { name: /Book C · part 3 → no part/ }));
    fireEvent.click(screen.getByRole("checkbox", { name: /Adopt source series name/ }));

    const applyButton = screen.getByRole("button", { name: /Apply/ });
    expect(applyButton).toHaveTextContent("Apply rename");
    expect(applyButton).toBeEnabled();

    fireEvent.click(applyButton);

    await waitFor(() => {
      expect(seriesApi.applySeriesPending).toHaveBeenCalledWith("Mistborn", {
        adoptSourceSeriesName: true,
        selections: [],
      });
    });
  });

  it("does not report a vanished apply as a success", async () => {
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
      seriesName: string;
    }) => void;
    expect(completeHandler).toBeDefined();

    // A completion with totalProcessed === 0 means the pending row was gone before the apply
    // ran (dismissed elsewhere, or superseded): an info notice, never a "Applied N" success.
    completeHandler({
      totalProcessed: 0,
      totalSucceeded: 0,
      totalFailed: 0,
      seriesName: "Mistborn",
    });

    await waitFor(() => {
      expect(notifications.info).toHaveBeenCalledWith(
        "The pending changes were already gone - nothing was applied",
      );
    });
  });

  it("reports the adopted name through onApplied when the apply completes with a rename", async () => {
    const onApplied = vi.fn();
    renderDialog(true, onApplied);
    await screen.findByText(/Book A · part 01 → 02/);

    fireEvent.click(screen.getByRole("button", { name: /Apply 2/ }));

    const completeCall = (mockSignalRValue as { on: ReturnType<typeof vi.fn> }).on.mock.calls.find(
      (call) => call[0] === "SeriesRefreshApplyComplete",
    );
    const completeHandler = completeCall?.[1] as (data: {
      totalProcessed: number;
      totalSucceeded: number;
      totalFailed: number;
      effectiveSeriesName?: string | null;
      seriesName: string;
    }) => void;
    expect(completeHandler).toBeDefined();

    // A fully successful adoption reports the new name so the caller can navigate its route.
    completeHandler({
      totalProcessed: 2,
      totalSucceeded: 2,
      totalFailed: 0,
      seriesName: "Mistborn",
      effectiveSeriesName: "Mistborn Saga",
    });

    await waitFor(() => {
      expect(onApplied).toHaveBeenCalledWith("Mistborn Saga");
    });
  });

  it("reports null through onApplied when the apply completed with no rename", async () => {
    const onApplied = vi.fn();
    renderDialog(true, onApplied);
    await screen.findByText(/Book A · part 01 → 02/);

    fireEvent.click(screen.getByRole("button", { name: /Apply 2/ }));

    const completeCall = (mockSignalRValue as { on: ReturnType<typeof vi.fn> }).on.mock.calls.find(
      (call) => call[0] === "SeriesRefreshApplyComplete",
    );
    const completeHandler = completeCall?.[1] as (data: {
      totalProcessed: number;
      totalSucceeded: number;
      totalFailed: number;
      effectiveSeriesName?: string | null;
      seriesName: string;
    }) => void;
    expect(completeHandler).toBeDefined();

    // No adoption ran: the series is still addressable under its original name.
    completeHandler({
      totalProcessed: 2,
      totalSucceeded: 2,
      totalFailed: 0,
      seriesName: "Mistborn",
    });

    await waitFor(() => {
      expect(onApplied).toHaveBeenCalledWith(null);
    });
  });

  it("ignores a completion for another series instead of closing or navigating this dialog", async () => {
    const onApplied = vi.fn();
    renderDialog(true, onApplied);
    await screen.findByText(/Book A · part 01 → 02/);

    // Arm `applying` like the other completion tests.
    fireEvent.click(screen.getByRole("button", { name: /Apply 2/ }));
    expect(screen.getByRole("button", { name: /Applying/ })).toBeInTheDocument();

    const completeCall = (mockSignalRValue as { on: ReturnType<typeof vi.fn> }).on.mock.calls.find(
      (call) => call[0] === "SeriesRefreshApplyComplete",
    );
    const completeHandler = completeCall?.[1] as (data: {
      totalProcessed: number;
      totalSucceeded: number;
      totalFailed: number;
      effectiveSeriesName?: string | null;
      seriesName: string;
    }) => void;
    expect(completeHandler).toBeDefined();

    // A different series' apply completed (e.g. started from the metadata-refresh page or a
    // parallel tab): this dialog must not close, toast the other series' counts or navigate.
    completeHandler({
      totalProcessed: 2,
      totalSucceeded: 2,
      totalFailed: 0,
      seriesName: "The Wheel of Time",
    });

    await waitFor(() => {
      expect(onApplied).not.toHaveBeenCalled();
    });
    expect(notifications.success).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: /Applying/ })).toBeInTheDocument();

    // The dialog is still applying for its own series, and its own completion still lands after
    // the ignored one - the ignore is a filter, not a state change.
    completeHandler({
      totalProcessed: 2,
      totalSucceeded: 2,
      totalFailed: 0,
      seriesName: "Mistborn",
    });

    await waitFor(() => {
      expect(onApplied).toHaveBeenCalledWith(null);
    });
    expect(notifications.success).toHaveBeenCalledWith("Applied 2 pending changes");
  });
});
