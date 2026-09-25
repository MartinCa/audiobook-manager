import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PendingOnlineMatchRowPanel } from "./PendingOnlineMatchRowPanel";
import { pendingOnlineMatchApi } from "@/services/api";
import { notifications } from "@/lib/notifications";
import type { PendingOnlineMatchListItem } from "@/types/PendingOnlineMatch";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  pendingOnlineMatchApi: {
    selectResult: vi.fn(),
  },
  metadataSearchApi: {
    getProxyImageUrl: vi.fn((url: string) => url),
  },
}));

const baseItem: PendingOnlineMatchListItem = {
  audiobookId: 1,
  bookName: "The Way of Kings",
  authors: ["Brandon Sanderson"],
  searchedAt: "2026-01-01T00:00:00Z",
  sourceNames: ["Audible"],
  results: [
    {
      url: "https://www.audible.com/pd/1",
      source: "Audible",
      authors: ["Brandon Sanderson"],
      narrators: ["Michael Kramer"],
      bookName: "The Way of Kings",
      genres: [],
    },
    {
      url: "https://www.goodreads.com/book/2",
      source: "Goodreads",
      authors: ["Brandon Sanderson"],
      narrators: [],
      bookName: "The Way of Kings (Goodreads)",
      genres: [],
    },
  ],
};

function renderPanel(item: PendingOnlineMatchListItem = baseItem, onResolved = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");
  render(
    <QueryClientProvider client={queryClient}>
      <PendingOnlineMatchRowPanel item={item} onResolved={onResolved} />
    </QueryClientProvider>,
  );
  return { invalidateSpy, onResolved };
}

describe("PendingOnlineMatchRowPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders one result card per candidate", () => {
    renderPanel();

    expect(screen.getByText("The Way of Kings")).toBeInTheDocument();
    expect(screen.getByText("The Way of Kings (Goodreads)")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /select this match/i })).toHaveLength(2);
  });

  it("shows a no-results message when the book has no candidates", () => {
    renderPanel({ ...baseItem, results: [] });

    expect(screen.getByText("No results found for this book.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /select this match/i })).not.toBeInTheDocument();
  });

  it("selecting a candidate calls selectResult with the book id and its index, invalidates views, and resolves", async () => {
    vi.mocked(pendingOnlineMatchApi.selectResult).mockResolvedValue(undefined);
    const { invalidateSpy, onResolved } = renderPanel();

    const buttons = screen.getAllByRole("button", { name: /select this match/i });
    fireEvent.click(buttons[1]!); // Goodreads candidate, index 1

    await waitFor(() => {
      expect(pendingOnlineMatchApi.selectResult).toHaveBeenCalledWith(1, 1);
    });
    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith(
        "Match selected — if there are changes to apply, they'll appear on the book's pending metadata banner.",
      );
    });
    expect(onResolved).toHaveBeenCalledTimes(1);
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["pendingOnlineMatch"] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["metadataRefresh"] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["books"] });
  });

  it("shows an error toast and does not resolve when selecting a candidate fails", async () => {
    vi.mocked(pendingOnlineMatchApi.selectResult).mockRejectedValue(new Error("Network error"));
    const { onResolved } = renderPanel();

    fireEvent.click(screen.getAllByRole("button", { name: /select this match/i })[0]!);

    await waitFor(() => {
      expect(notifications.error).toHaveBeenCalled();
    });
    expect(onResolved).not.toHaveBeenCalled();
  });

  it("disables every select button while one selection is in flight", async () => {
    let resolveSelect: (() => void) | undefined;
    vi.mocked(pendingOnlineMatchApi.selectResult).mockReturnValue(
      new Promise<void>((resolve) => {
        resolveSelect = resolve;
      }),
    );
    renderPanel();

    const buttons = screen.getAllByRole("button", { name: /select this match/i });
    fireEvent.click(buttons[0]!);

    await waitFor(() => {
      expect(buttons[1]).toBeDisabled();
    });

    resolveSelect?.();
  });
});
