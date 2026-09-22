import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SimilarValues } from "./SimilarValues";
import { SignalREvents } from "@/constants/signalrEvents";
import { SignalRContext } from "@/context/SignalRContext";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";
import { similarValuesApi } from "@/services/api";
import type { HubEventHandler, SignalRContextValue } from "@/context/SignalRContext";

vi.mock("@/services/api", () => ({
  similarValuesApi: {
    getSimilarAuthors: vi.fn(),
    getSimilarSeries: vi.fn(),
    align: vi.fn(),
    getIgnoredPairs: vi.fn(),
    ignorePair: vi.fn(),
    removeIgnoredPair: vi.fn(),
  },
  operationsApi: {
    getStatus: vi.fn().mockResolvedValue({ isRunning: false, processed: 0, total: 0 }),
  },
}));

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false } },
});

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

function renderWithProviders(ui: React.ReactElement) {
  return render(
    <SignalRContext.Provider value={signalR as SignalRContextValue}>
      <QueryClientProvider client={queryClient}>
        <RouterTestWrapper ui={ui} />
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
}

const groupPage = {
  items: [
    {
      candidates: [
        { value: "J.K. Rowling", bookCount: 7 },
        { value: "JK Rowling", bookCount: 2 },
      ],
    },
  ],
  totalCount: 3,
};

describe("SimilarValues", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    signalR = makeSignalR();
    vi.mocked(similarValuesApi.getSimilarAuthors).mockResolvedValue(groupPage);
    vi.mocked(similarValuesApi.getSimilarSeries).mockResolvedValue(groupPage);
    vi.mocked(similarValuesApi.getIgnoredPairs).mockResolvedValue([]);
    vi.mocked(similarValuesApi.ignorePair).mockResolvedValue(undefined);
  });

  it("renders tabs, group variants and per-candidate book counts from the server page", async () => {
    renderWithProviders(<SimilarValues />);

    expect(await screen.findByText("Similar Authors")).toBeInTheDocument();
    expect(screen.getByText("Similar Series")).toBeInTheDocument();

    expect(await screen.findByText("J.K. Rowling")).toBeInTheDocument();
    expect(screen.getByText("2 variants")).toBeInTheDocument();
    expect(screen.getByText("7 books")).toBeInTheDocument();
  });

  it("switches to the series endpoint when the tab changes", async () => {
    renderWithProviders(<SimilarValues />);

    const seriesTab = await screen.findByText("Similar Series");
    seriesTab.click();

    await waitFor(() => {
      expect(similarValuesApi.getSimilarSeries).toHaveBeenCalledWith(0, 50);
    });
  });

  it("shows a pager when the detection finds more groups than one page", async () => {
    vi.mocked(similarValuesApi.getSimilarAuthors).mockResolvedValue({
      items: groupPage.items,
      totalCount: 130,
    });

    renderWithProviders(<SimilarValues />);

    expect(await screen.findByText(/Showing 1–50 of 130 groups/)).toBeInTheDocument();
    const nextButton = screen.getByRole("button", { name: "Next" });
    expect(nextButton).toBeEnabled();
    nextButton.click();

    await waitFor(() => {
      expect(similarValuesApi.getSimilarAuthors).toHaveBeenCalledWith(1, 50);
    });
  });

  // Regression for the review finding: an alignment merge shrinks the group total, so a user
  // sitting on a later page had its refetch ask for a page that no longer exists - it came back
  // empty and the section was stuck on a dead-end empty state. Completion drops back to page 0.
  it("drops back to page 0 when an alignment completes", async () => {
    let total = 130;
    vi.mocked(similarValuesApi.getSimilarAuthors).mockImplementation(() =>
      Promise.resolve({ items: groupPage.items, totalCount: total }),
    );

    renderWithProviders(<SimilarValues />);

    expect(await screen.findByText(/Showing 1–50 of 130 groups/)).toBeInTheDocument();
    screen.getByRole("button", { name: "Next" }).click();
    await waitFor(() => {
      expect(similarValuesApi.getSimilarAuthors).toHaveBeenCalledWith(1, 50);
    });

    // The alignment merges groups: the total shrinks and the completion event arrives.
    total = 60;
    signalR.emit(SignalREvents.SimilarValueAlignComplete, {
      totalProcessed: 70,
      totalSucceeded: 70,
      totalFailed: 0,
    });

    await waitFor(() => {
      expect(similarValuesApi.getSimilarAuthors).toHaveBeenCalledWith(0, 50);
    });
    expect(await screen.findByText(/Showing 1–50 of 60 groups/)).toBeInTheDocument();
  });

  it("marking a candidate as not similar calls the API with the rest of its group and refetches", async () => {
    renderWithProviders(<SimilarValues />);

    const notSimilarButton = await screen.findByRole("button", {
      name: 'Mark "J.K. Rowling" as not similar to the rest of this group',
    });
    notSimilarButton.click();

    await waitFor(() => {
      expect(similarValuesApi.ignorePair).toHaveBeenCalledWith("author", "J.K. Rowling", [
        "JK Rowling",
      ]);
    });
    // Invalidating similarValues.all() must refetch the currently-shown page.
    await waitFor(() => {
      expect(similarValuesApi.getSimilarAuthors).toHaveBeenCalledTimes(2);
    });
  });

  it("Show ignored opens the ignored-pairs dialog for the active tab", async () => {
    vi.mocked(similarValuesApi.getIgnoredPairs).mockResolvedValue([
      { id: 1, valueA: "Ben Winters", valueB: "Ed Winters", ignoredAtUtc: "2026-01-01T00:00:00Z" },
    ]);

    renderWithProviders(<SimilarValues />);

    (await screen.findByRole("button", { name: "Show ignored" })).click();

    expect(await screen.findByText("Ben Winters")).toBeInTheDocument();
    expect(screen.getByText("Ed Winters")).toBeInTheDocument();
    expect(similarValuesApi.getIgnoredPairs).toHaveBeenCalledWith("author");
  });
});
