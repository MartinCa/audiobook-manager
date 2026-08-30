import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SimilarValues } from "./SimilarValues";
import { SignalRContext } from "@/context/SignalRContext";

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
        <MemoryRouter>{ui}</MemoryRouter>
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
}

describe("SimilarValues", () => {
  it("renders tabs for Similar Authors and Similar Series", () => {
    renderWithProviders(<SimilarValues />);
    expect(screen.getByText("Similar Authors")).toBeInTheDocument();
    expect(screen.getByText("Similar Series")).toBeInTheDocument();
  });
});
