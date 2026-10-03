import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { IgnoredSimilarValuesDialog } from "./IgnoredSimilarValuesDialog";
import { similarValuesApi } from "@/services/api";
import { notifications } from "@/lib/notifications";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  similarValuesApi: {
    getIgnoredPairs: vi.fn(),
    removeIgnoredPair: vi.fn(),
  },
}));

function renderDialog(open = true) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <IgnoredSimilarValuesDialog open={open} onOpenChange={vi.fn()} valueType="author" />
    </QueryClientProvider>,
  );
}

describe("IgnoredSimilarValuesDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders each ignored pair as a row", async () => {
    vi.mocked(similarValuesApi.getIgnoredPairs).mockResolvedValue([
      { id: 1, valueA: "Ben Winters", valueB: "Ed Winters", ignoredAtUtc: "2026-01-01T00:00:00Z" },
      { id: 2, valueA: "A Author", valueB: "B Author", ignoredAtUtc: "2026-01-02T00:00:00Z" },
    ]);

    renderDialog();

    expect(await screen.findByText("Ben Winters")).toBeInTheDocument();
    expect(screen.getByText("Ed Winters")).toBeInTheDocument();
    expect(screen.getByText("A Author")).toBeInTheDocument();
    expect(screen.getByText("B Author")).toBeInTheDocument();
  });

  it("shows an empty state when there are no ignored pairs", async () => {
    vi.mocked(similarValuesApi.getIgnoredPairs).mockResolvedValue([]);

    renderDialog();

    expect(await screen.findByText("No ignored pairs yet.")).toBeInTheDocument();
  });

  it("removing a pair calls the API with its id and valueType", async () => {
    vi.mocked(similarValuesApi.getIgnoredPairs).mockResolvedValue([
      { id: 7, valueA: "Ben Winters", valueB: "Ed Winters", ignoredAtUtc: "2026-01-01T00:00:00Z" },
    ]);
    vi.mocked(similarValuesApi.removeIgnoredPair).mockResolvedValue(undefined);

    renderDialog();

    const removeButton = await screen.findByRole("button", { name: "Remove ignored pair" });
    removeButton.click();

    await waitFor(() => {
      expect(similarValuesApi.removeIgnoredPair).toHaveBeenCalledWith("author", 7);
    });
  });

  it("toasts and tints the remove button as failed when the API rejects", async () => {
    vi.mocked(similarValuesApi.getIgnoredPairs).mockResolvedValue([
      { id: 7, valueA: "Ben Winters", valueB: "Ed Winters", ignoredAtUtc: "2026-01-01T00:00:00Z" },
    ]);
    vi.mocked(similarValuesApi.removeIgnoredPair).mockRejectedValue(new Error("boom"));

    renderDialog();

    (await screen.findByRole("button", { name: "Remove ignored pair" })).click();

    const failed = await screen.findByRole("button", { name: "Could not remove ignored pair" });
    expect(failed).toHaveAttribute("data-status", "error");
    expect(notifications.error).toHaveBeenCalledWith("boom");
    expect(notifications.success).not.toHaveBeenCalled();
  });

  it("does not fetch when the dialog is closed", () => {
    renderDialog(false);
    expect(similarValuesApi.getIgnoredPairs).not.toHaveBeenCalled();
  });
});
