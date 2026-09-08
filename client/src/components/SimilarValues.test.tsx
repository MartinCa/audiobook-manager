import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SimilarValues } from "./SimilarValues";
import { SignalRContext } from "@/context/SignalRContext";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";
import { similarValuesApi } from "@/services/api";

vi.mock("@/services/api", () => ({
  similarValuesApi: {
    getSimilarAuthors: vi.fn(),
    getSimilarSeries: vi.fn(),
    align: vi.fn(),
  },
  operationsApi: {
    getStatus: vi.fn().mockResolvedValue({ isRunning: false, processed: 0, total: 0 }),
  },
}));

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false } },
});

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

function renderWithProviders(ui: React.ReactElement) {
  return render(
    <SignalRContext.Provider value={mockSignalRValue}>
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
    vi.mocked(similarValuesApi.getSimilarAuthors).mockResolvedValue(groupPage);
    vi.mocked(similarValuesApi.getSimilarSeries).mockResolvedValue(groupPage);
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
});
