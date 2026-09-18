import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Settings } from "./Settings";

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children, ...props }: React.ComponentProps<"a">) => <a {...props}>{children}</a>,
}));

vi.mock("@/services/api", () => ({
  settingsApi: {
    getSystemInfo: vi.fn().mockResolvedValue({
      version: "0.9.0",
      commitHash: "abc1234",
      dotNetVersion: ".NET 10.0.0",
    }),
  },
}));

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("Settings", () => {
  it("renders System Information and no series mapping UI", async () => {
    renderWithProviders(<Settings />);

    expect(screen.getByRole("heading", { name: "System Information" })).toBeInTheDocument();
    expect(screen.getByText("About & System Information")).toBeInTheDocument();

    // The regex mappings moved to each series' Management section; the Settings page keeps only
    // system info and must not reference the removed mapping surface.
    expect(screen.queryByText(/Series Mapping/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add Series Mapping" })).not.toBeInTheDocument();

    expect(await screen.findByText("v0.9.0")).toBeInTheDocument();
    expect(screen.getByText("abc1234")).toBeInTheDocument();
    expect(screen.getByText(".NET 10.0.0")).toBeInTheDocument();
    expect(screen.getByText("SQLite (via EF Core)")).toBeInTheDocument();
  });
});
