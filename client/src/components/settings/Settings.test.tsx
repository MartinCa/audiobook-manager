import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Settings } from "./Settings";

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children, ...props }: React.ComponentProps<"a">) => <a {...props}>{children}</a>,
}));

vi.mock("@/services/api", () => ({
  settingsApi: {
    getSeriesMappings: vi.fn().mockResolvedValue([]),
    getSystemInfo: vi.fn().mockResolvedValue({
      version: "0.9.0",
      commitHash: "abc1234",
      dotNetVersion: ".NET 10.0.0",
    }),
  },
  similarValuesApi: {
    getSeriesNames: vi.fn().mockResolvedValue([]),
  },
}));

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("Settings", () => {
  it("renders Settings page with Series Mapping and About & System Information", async () => {
    renderWithProviders(<Settings />);

    expect(screen.getByRole("heading", { name: "Settings — Series Mappings" })).toBeInTheDocument();
    expect(screen.getByText(/Series Regex Mappings/)).toBeInTheDocument();
    expect(screen.getByText("About & System Information")).toBeInTheDocument();

    expect(await screen.findByText("v0.9.0")).toBeInTheDocument();
    expect(screen.getByText("abc1234")).toBeInTheDocument();
    expect(screen.getByText(".NET 10.0.0")).toBeInTheDocument();
    expect(screen.getByText("SQLite (via EF Core)")).toBeInTheDocument();
  });
});
