import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SeriesDetail } from "./SeriesDetail";
import { SignalRContext } from "@/context/SignalRContext";
import { seriesApi } from "@/services/api";

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

function renderWithProviders(initialEntry = "/library/series/Mistborn") {
  return render(
    <SignalRContext.Provider value={mockSignalRValue}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[initialEntry]}>
          <Routes>
            <Route path="/library/series/:seriesName" element={<SeriesDetail />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
}

describe("SeriesDetail", () => {
  it("renders series detail with matched provider and books", async () => {
    vi.spyOn(seriesApi, "getSeriesDetail").mockResolvedValue({
      overview: {
        id: 1,
        name: "Mistborn",
        authors: ["Brandon Sanderson"],
        ownedBookCount: 3,
        isMatched: true,
        matchedSourceName: "Hardcover",
        matchedSourceId: "123",
        matchedSourceUrl: "https://hardcover.app/series/mistborn",
        matchConfidence: 0.98,
        lastRefreshedAt: "2026-01-01T00:00:00Z",
        expectedBookCount: 4,
        missingBookCount: 1,
        ignoredBookCount: 0,
        includeOmnibusEditions: false,
      },
      ownedBooks: [
        {
          id: 10,
          bookName: "The Final Empire",
          seriesPart: "1",
          year: 2006,
          authors: ["Brandon Sanderson"],
          narrators: ["Michael Kramer"],
          durationInSeconds: 88000,
        },
      ],
      missingBooks: [
        {
          id: 20,
          title: "The Alloy of Law",
          position: "4",
          year: 2011,
          sourceUrl: null,
          isIgnored: false,
        },
      ],
      ignoredBooks: [],
    });

    renderWithProviders();

    expect(await screen.findByText(/The Final Empire/)).toBeInTheDocument();
    expect(screen.getByText("Matched to Hardcover")).toBeInTheDocument();
    expect(screen.getByText(/The Alloy of Law/)).toBeInTheDocument();
    expect(screen.getByText("Ignore")).toBeInTheDocument();
    expect(
      screen.getByText("Include omnibus/box-set editions in missing books list"),
    ).toBeInTheDocument();
  });
});
