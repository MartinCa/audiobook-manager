import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BulkMissingBookMatchDialog } from "./BulkMissingBookMatchDialog";
import { SignalREvents } from "@/constants/signalrEvents";
import { SignalRContext } from "@/context/SignalRContext";
import type { HubEventHandler, SignalRContextValue } from "@/context/SignalRContext";
import type { SeriesBulkCandidateItem, SeriesBookCandidate } from "@/types/Series";

vi.mock("@/services/api", () => ({
  seriesApi: {
    getBulkMissingBookCandidates: vi.fn(),
    startBulkMissingBookApply: vi.fn(),
  },
  operationsApi: {
    getStatus: vi.fn().mockResolvedValue({ isRunning: false, processed: 0, total: 0 }),
  },
}));

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

import { notifications } from "@/lib/notifications";
import { seriesApi, operationsApi } from "@/services/api";

const candidateA: SeriesBookCandidate = {
  audiobookId: 10,
  bookName: "The Well of Ascension",
  year: 2007,
  authors: ["Brandon Sanderson"],
  series: null,
  seriesPart: null,
  titleSimilarity: 0.95,
  authorMatches: true,
};

const candidateB: SeriesBookCandidate = {
  audiobookId: 20,
  bookName: "Well of Ascension (Alternate)",
  year: 2007,
  authors: ["Someone Else"],
  series: null,
  seriesPart: null,
  titleSimilarity: 0.7,
  authorMatches: false,
};

function missingItem(
  id: number,
  title: string,
  position: string | null,
  candidates: SeriesBookCandidate[],
): SeriesBulkCandidateItem {
  return {
    book: { id, title, position, year: 2000 + id, sourceUrl: null, isIgnored: false },
    candidates,
  };
}

function page(items: SeriesBulkCandidateItem[], totalCount: number) {
  return { items, totalCount };
}

function makeSignalR() {
  const handlers = new Map<string, HubEventHandler<unknown>[]>();
  return {
    connection: null,
    isConnected: false,
    on: vi.fn((event: string, handler: HubEventHandler<unknown>) => {
      const list = handlers.get(event) ?? [];
      list.push(handler);
      handlers.set(event, list);
    }),
    off: vi.fn((event: string, handler: HubEventHandler<unknown>) => {
      handlers.set(
        event,
        (handlers.get(event) ?? []).filter((h) => h !== handler),
      );
    }),
    onReconnected: vi.fn(),
    offReconnected: vi.fn(),
    emit: (event: string, data: unknown) => {
      for (const handler of handlers.get(event) ?? []) handler(data);
    },
  };
}

let signalR: ReturnType<typeof makeSignalR>;

function renderModal(onOpenChange: (open: boolean) => void = vi.fn(), open = true) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <SignalRContext.Provider value={signalR as SignalRContextValue}>
      <QueryClientProvider client={queryClient}>
        <BulkMissingBookMatchDialog open={open} onOpenChange={onOpenChange} seriesName="Mistborn" />
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
}

describe("BulkMissingBookMatchDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    signalR = makeSignalR();
    // clearAllMocks does not reset implementations, so a test that overrides the idle status (or
    // one that resolves the candidate query) would leak it into every later test.
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: false,
      processed: 0,
      total: 0,
    });
  });

  it("renders every missing book with the best candidate preselected", async () => {
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page(
        [
          missingItem(1, "The Well of Ascension", "2", [candidateA, candidateB]),
          missingItem(2, "The Hero of Ages", "3", [candidateB]),
        ],
        2,
      ),
    );

    renderModal();

    expect(await screen.findByText("Part 2 — The Well of Ascension")).toBeInTheDocument();
    expect(screen.getByText("Part 3 — The Hero of Ages")).toBeInTheDocument();

    // Each row's (closed) trigger shows the server-ranked best candidate, and the Apply button
    // counts every preselected row as assigned.
    expect(
      screen.getByText("The Well of Ascension — Brandon Sanderson (95% title)"),
    ).toBeInTheDocument();
    expect(
      screen.getAllByText("Well of Ascension (Alternate) — Someone Else (70% title)"),
    ).toHaveLength(1);
    expect(screen.getByRole("button", { name: "Apply 2 Assignments" })).toBeInTheDocument();
  });

  it("a missing book with no candidates is preselected to Do not assign and not counted", async () => {
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page([missingItem(1, "The Well of Ascension", "2", [])], 1),
    );

    renderModal();

    await screen.findByText("Part 2 — The Well of Ascension");

    const apply = screen.getByRole("button", { name: /Apply 0 Assignments/ });
    expect(apply).toBeDisabled();
    expect(screen.getByText("Do not assign")).toBeInTheDocument();
  });

  it("Apply sends only the accepted selections, addressed by the roster entry's natural key", async () => {
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page(
        [
          missingItem(1, "The Well of Ascension", "2", [candidateA]),
          missingItem(2, "The Hero of Ages", "3", []),
        ],
        2,
      ),
    );

    renderModal();

    await screen.findByText("Part 2 — The Well of Ascension");

    fireEvent.click(screen.getByRole("button", { name: "Apply 1 Assignment" }));

    await waitFor(() => {
      expect(seriesApi.startBulkMissingBookApply).toHaveBeenCalledWith("Mistborn", [
        { position: "2", title: "The Well of Ascension", audiobookId: 10 },
      ]);
    });
  });

  it("paging requests the next page and keeps selections", async () => {
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockImplementation((_name, requestPage) =>
      Promise.resolve(
        requestPage === 0
          ? page([missingItem(1, "The Well of Ascension", "2", [candidateA])], 51)
          : page([missingItem(2, "The Hero of Ages", "3", [candidateB])], 51),
      ),
    );

    renderModal();

    await screen.findByText("Part 2 — The Well of Ascension");
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    await waitFor(() => {
      expect(seriesApi.getBulkMissingBookCandidates).toHaveBeenCalledWith("Mistborn", 1, 50);
    });
    expect(await screen.findByText("Part 3 — The Hero of Ages")).toBeInTheDocument();

    // The page-0 record's preselection survives navigating to page 1 (the selection state is
    // keyed by the roster entry's natural key, not the page).
    expect(screen.getByRole("button", { name: "Apply 2 Assignments" })).toBeInTheDocument();
  });

  it("shows progress while applying and closes on completion", async () => {
    const onOpenChange = vi.fn();
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page([missingItem(1, "The Well of Ascension", "2", [candidateA])], 1),
    );

    renderModal(onOpenChange);

    await screen.findByText("Part 2 — The Well of Ascension");

    fireEvent.click(screen.getByRole("button", { name: "Apply 1 Assignment" }));

    signalR.emit(SignalREvents.SeriesMissingBookApplyProgress, {
      processed: 1,
      total: 1,
      succeeded: 1,
      failed: 0,
    });

    expect(await screen.findByText(/Applying \(1 succeeded, 0 failed\)/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Applying..." })).toBeDisabled();

    signalR.emit(SignalREvents.SeriesMissingBookApplyComplete, {
      totalProcessed: 1,
      totalSucceeded: 1,
      totalFailed: 0,
    });

    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
    expect(notifications.success).toHaveBeenCalled();
  });

  it("reports a failure summary on completion with failures", async () => {
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page([missingItem(1, "The Well of Ascension", "2", [candidateA])], 1),
    );

    renderModal();

    await screen.findByText("Part 2 — The Well of Ascension");

    signalR.emit(SignalREvents.SeriesMissingBookApplyComplete, {
      totalProcessed: 1,
      totalSucceeded: 0,
      totalFailed: 1,
    });

    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith(
        expect.stringContaining("1 failed") as string,
      );
    });
  });

  it("resurfaces an in-progress apply from the operation status on open", async () => {
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page([missingItem(1, "The Well of Ascension", "2", [candidateA])], 1),
    );
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: true,
      processed: 1,
      total: 3,
    });

    renderModal();

    await screen.findByText("Part 2 — The Well of Ascension");

    // The status endpoint only carries processed/total, so the label says "Resuming" rather than
    // fabricating a "(0 succeeded, 0 failed)" summary that misreports the batch.
    expect(await screen.findByText("Resuming apply in progress")).toBeInTheDocument();
    expect(screen.getByText("1 / 3 (33%)")).toBeInTheDocument();
    expect(seriesApi.startBulkMissingBookApply).not.toHaveBeenCalled();
  });

  it("preselects the best candidate that is not already assigned to another book", async () => {
    // Both rows rank the same library book first; blindly taking candidates[0] for both would
    // hand the apply a duplicate assignment (a batch the server refuses). The second row must
    // skip the taken book and take its remaining candidate instead.
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page(
        [
          missingItem(1, "The Well of Ascension", "2", [candidateA, candidateB]),
          missingItem(2, "The Hero of Ages", "3", [candidateA, candidateB]),
        ],
        2,
      ),
    );

    renderModal();

    await screen.findByText("Part 2 — The Well of Ascension");

    fireEvent.click(screen.getByRole("button", { name: "Apply 2 Assignments" }));

    await waitFor(() => {
      expect(seriesApi.startBulkMissingBookApply).toHaveBeenCalledWith("Mistborn", [
        { position: "2", title: "The Well of Ascension", audiobookId: 10 },
        { position: "3", title: "The Hero of Ages", audiobookId: 20 },
      ]);
    });
  });

  it("leaves a missing book unassigned when every candidate is already taken", async () => {
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page(
        [
          missingItem(1, "The Well of Ascension", "2", [candidateA]),
          missingItem(2, "The Hero of Ages", "3", [candidateA]),
        ],
        2,
      ),
    );

    renderModal();

    await screen.findByText("Part 2 — The Well of Ascension");

    // Row 2's only candidate is row 1's pick, so it must start as "Do not assign" and stay out of
    // the apply count rather than duplicating the assignment.
    expect(screen.getByRole("button", { name: "Apply 1 Assignment" })).toBeInTheDocument();
    expect(screen.getAllByText("Do not assign")).toHaveLength(1);
  });

  it("refuses an explicit selection that would assign one library book to two rows", async () => {
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page(
        [
          missingItem(1, "The Well of Ascension", "2", [candidateA, candidateB]),
          missingItem(2, "The Hero of Ages", "3", [candidateB]),
        ],
        2,
      ),
    );

    renderModal();

    await screen.findByText("Part 2 — The Well of Ascension");

    // Row 2 preselected candidateB (20); giving the identical library book to row 1 must be
    // refused with a message naming the row it currently belongs to.
    fireEvent.click(
      screen.getByRole("combobox", {
        name: "Library candidate for Part 2 — The Well of Ascension",
      }),
    );
    const sharedOption = screen.getByRole("option", {
      name: "Well of Ascension (Alternate) — Someone Else (70% title)",
    });
    // Base UI commits a real-mouse click only for an option the pointer has touched first, so the
    // jsdom test replays the browser sequence: pointerdown to arm the option, then click.
    fireEvent.pointerDown(sharedOption);
    fireEvent.click(sharedOption);

    await waitFor(() => {
      expect(notifications.error).toHaveBeenCalledWith(
        expect.stringContaining("already assigned to") as string,
      );
    });

    // Row 1 keeps its prior selection (its trigger still shows candidateA) and the apply still
    // counts two distinct books.
    expect(
      screen.getByRole("combobox", {
        name: "Library candidate for Part 2 — The Well of Ascension",
      }).textContent,
    ).toContain("The Well of Ascension — Brandon Sanderson (95% title)");
    expect(screen.getByRole("button", { name: "Apply 2 Assignments" })).toBeInTheDocument();
  });

  it("gives every candidate Select an accessible label naming its missing book", async () => {
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page(
        [
          missingItem(1, "The Well of Ascension", "2", [candidateA]),
          missingItem(2, "The Hero of Ages", "3", [candidateB]),
        ],
        2,
      ),
    );

    renderModal();

    await screen.findByText("Part 2 — The Well of Ascension");

    expect(
      screen.getByRole("combobox", {
        name: "Library candidate for Part 2 — The Well of Ascension",
      }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("combobox", {
        name: "Library candidate for Part 3 — The Hero of Ages",
      }),
    ).toBeInTheDocument();
  });

  it("shows how many of the total missing books the apply will assign", async () => {
    // The lazy preselection only ever covers reviewed rows, so the footer must make the scope of
    // the apply visible: N of the total, not "all of them".
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page([missingItem(1, "The Well of Ascension", "2", [candidateA])], 51),
    );

    renderModal();

    await screen.findByText("Part 2 — The Well of Ascension");

    expect(screen.getByText(/Assigns 1 of 51 missing books/)).toBeInTheDocument();
  });

  it("resyncs operation status when the dialog opens after the status changed", async () => {
    // The dialog is permanently mounted in SeriesDetail: only its portal toggles on open. The
    // mount-time status fetch runs at page load (idle here), so a bulk apply that starts
    // elsewhere while the dialog is closed must be picked up by the open transition - not only
    // by mount or a SignalR reconnect, which never fire again.
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page([missingItem(1, "The Well of Ascension", "2", [candidateA])], 1),
    );
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: false,
      processed: 0,
      total: 0,
    });

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const tree = (open: boolean) => (
      <SignalRContext.Provider value={signalR as SignalRContextValue}>
        <QueryClientProvider client={queryClient}>
          <BulkMissingBookMatchDialog open={open} onOpenChange={vi.fn()} seriesName="Mistborn" />
        </QueryClientProvider>
      </SignalRContext.Provider>
    );
    const { rerender } = render(tree(false));

    // The operation went into flight after the page loaded (and after the mount-time fetch).
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: true,
      processed: 1,
      total: 3,
    });

    rerender(tree(true));

    expect(await screen.findByText("Resuming apply in progress")).toBeInTheDocument();
    expect(seriesApi.startBulkMissingBookApply).not.toHaveBeenCalled();
  });

  it("mounting the dialog already open issues a single status fetch", async () => {
    // Guard against the initial-status race: when the dialog is rendered open from the start,
    // the mount-time fetch is the only one - the open-transition resync must not fire on top of
    // it, or two concurrent status GETs could resolve out of order and resurrect a stale state.
    vi.mocked(seriesApi.getBulkMissingBookCandidates).mockResolvedValue(
      page([missingItem(1, "The Well of Ascension", "2", [candidateA])], 1),
    );
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: true,
      processed: 1,
      total: 3,
    });

    renderModal();

    expect(await screen.findByText("Resuming apply in progress")).toBeInTheDocument();
    expect(operationsApi.getStatus).toHaveBeenCalledTimes(1);
  });
});
