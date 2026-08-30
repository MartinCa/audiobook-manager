import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SimilarValues } from "./SimilarValues";
import { SignalRContext } from "@/context/SignalRContext";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";

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

describe("SimilarValues", () => {
  it("renders tabs for Similar Authors and Similar Series", async () => {
    renderWithProviders(<SimilarValues />);
    expect(await screen.findByText("Similar Authors")).toBeInTheDocument();
    expect(screen.getByText("Similar Series")).toBeInTheDocument();
  });
});
