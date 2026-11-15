import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MakePrimarySeriesAction } from "./MakePrimarySeriesAction";
import { notifications } from "@/lib/notifications";
import { bulkEditApi } from "@/services/api";
import type { BookSelection } from "@/hooks/useBookSelection";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  bulkEditApi: { apply: vi.fn() },
}));

function selectionOf(...ids: number[]): BookSelection {
  return {
    count: ids.length,
    selectedBooks: ids.map((id) => ({ id, title: `Book ${id}`, authors: ["A One"] })),
    isSelected: (id) => ids.includes(id),
    toggle: vi.fn(),
    selectPage: vi.fn(),
    deselectPage: vi.fn(),
    pageAllSelected: () => false,
    pageSomeSelected: () => false,
    clear: vi.fn(),
  };
}

function renderAction(selection: BookSelection, disabled = false) {
  render(
    <MakePrimarySeriesAction
      seriesName="Sword Art Online / Progressive"
      selection={selection}
      disabled={disabled}
    />,
  );
}

describe("MakePrimarySeriesAction", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(bulkEditApi.apply).mockResolvedValue(undefined);
  });

  it("explains what will happen and sends nothing until it is confirmed", () => {
    renderAction(selectionOf(4, 9));

    fireEvent.click(screen.getByRole("button", { name: "Make primary series" }));

    expect(screen.getByText(/Sword Art Online \/ Progressive/)).toBeInTheDocument();
    expect(screen.getByText(/the 2 selected books/)).toBeInTheDocument();
    expect(screen.getByText(/re-tagged and may be moved/)).toBeInTheDocument();
    expect(bulkEditApi.apply).not.toHaveBeenCalled();
  });

  it("sends a primarySeries set with exactly the selected ids and the page's series, and nothing else", async () => {
    renderAction(selectionOf(4, 9));

    fireEvent.click(screen.getByRole("button", { name: "Make primary series" }));
    fireEvent.click(screen.getByRole("button", { name: "Make primary" }));

    await waitFor(() => {
      expect(bulkEditApi.apply).toHaveBeenCalledWith([4, 9], {
        audiobookIds: [4, 9],
        primarySeries: { action: "set", value: "Sword Art Online / Progressive" },
      });
    });
    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith(
        'Making "Sword Art Online / Progressive" the primary series of 2 books',
      );
    });
    await waitFor(() => {
      expect(screen.queryByRole("button", { name: "Make primary" })).not.toBeInTheDocument();
    });
  });

  it("uses the singular for one book", async () => {
    renderAction(selectionOf(4));

    fireEvent.click(screen.getByRole("button", { name: "Make primary series" }));
    fireEvent.click(screen.getByRole("button", { name: "Make primary" }));

    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith(
        'Making "Sword Art Online / Progressive" the primary series of 1 book',
      );
    });
  });

  it("keeps the dialog open and toasts the error when the request is refused", async () => {
    vi.mocked(bulkEditApi.apply).mockRejectedValue(new Error("boom"));
    renderAction(selectionOf(4));

    fireEvent.click(screen.getByRole("button", { name: "Make primary series" }));
    fireEvent.click(screen.getByRole("button", { name: "Make primary" }));

    await waitFor(() => expect(notifications.error).toHaveBeenCalled());
    expect(screen.getByRole("button", { name: "Make primary" })).toBeInTheDocument();
    expect(notifications.success).not.toHaveBeenCalled();
  });

  it("cannot be opened while another operation runs", () => {
    renderAction(selectionOf(4), true);

    expect(screen.getByRole("button", { name: "Make primary series" })).toBeDisabled();
  });
});
