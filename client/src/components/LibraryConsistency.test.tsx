import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { LibraryConsistency } from "./LibraryConsistency";
import { SignalRContext } from "@/context/SignalRContext";
import { consistencyApi } from "@/services/api";

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

describe("LibraryConsistency", () => {
  it("renders run check button and consistency header", async () => {
    vi.spyOn(consistencyApi, "getIssues").mockResolvedValue([]);
    vi.spyOn(consistencyApi, "getOrphanDirectories").mockResolvedValue([]);

    renderWithProviders(<LibraryConsistency />);
    expect(screen.getByText("Run Consistency Check")).toBeInTheDocument();
    expect(await screen.findByText("Library Consistency")).toBeInTheDocument();
  });
});
