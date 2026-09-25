import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BulkOnlineMatchSearchDialog } from "./BulkOnlineMatchSearchDialog";
import { metadataSearchApi } from "@/services/api";

vi.mock("@/services/api", () => ({
  metadataSearchApi: {
    getServices: vi.fn().mockResolvedValue([
      { name: "Audible", enabled: true },
      { name: "Goodreads", enabled: true },
    ]),
  },
}));

function renderDialog(props: Partial<Parameters<typeof BulkOnlineMatchSearchDialog>[0]> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onOpenChange = vi.fn();
  const onConfirm = vi.fn();
  render(
    <QueryClientProvider client={queryClient}>
      <BulkOnlineMatchSearchDialog
        open
        onOpenChange={onOpenChange}
        bookCount={3}
        onConfirm={onConfirm}
        {...props}
      />
    </QueryClientProvider>,
  );
  return { onOpenChange, onConfirm };
}

describe("BulkOnlineMatchSearchDialog", () => {
  it("shows the book count in the description", async () => {
    renderDialog({ bookCount: 3 });

    expect(await screen.findByText(/Searches 3 selected books/)).toBeInTheDocument();
  });

  it("uses singular wording for a single book", async () => {
    renderDialog({ bookCount: 1 });

    expect(await screen.findByText(/Searches 1 selected book/)).toBeInTheDocument();
  });

  it("lists the available metadata sources", async () => {
    renderDialog();

    expect(await screen.findByText("Audible")).toBeInTheDocument();
    expect(screen.getByText("Goodreads")).toBeInTheDocument();
  });

  it("calls onConfirm with the active sources and closes the dialog", async () => {
    const { onOpenChange, onConfirm } = renderDialog();
    await screen.findByText("Audible");

    fireEvent.click(screen.getByRole("button", { name: /start search/i }));

    await waitFor(() => {
      expect(onConfirm).toHaveBeenCalledWith(expect.arrayContaining(["Audible", "Goodreads"]));
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("calls onOpenChange(false) on Cancel without confirming", async () => {
    const { onOpenChange, onConfirm } = renderDialog();
    await screen.findByText("Audible");

    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  // Regression test: unlike BookSearchDialog's interactive dialog, this one commits a background
  // run over every selected book on confirm - re-expanding an intentional "nothing selected" back
  // to "everything" (as BookSearchDialog's own copy of this logic does) would search sources the
  // user just turned off, silently, with no way to notice or undo it mid-run.
  it("disables Start Search, rather than silently re-selecting every source, once the user toggles every source off", async () => {
    renderDialog();
    await screen.findByText("Audible");

    fireEvent.click(screen.getByText("Audible"));
    fireEvent.click(screen.getByText("Goodreads"));

    expect(screen.getByRole("button", { name: /start search/i })).toBeDisabled();
  });

  it("disables Start Search when no metadata source is configured", async () => {
    vi.mocked(metadataSearchApi.getServices).mockResolvedValueOnce([
      { name: "Hardcover", enabled: false, disabledReason: "No API key configured" },
    ]);
    renderDialog();

    await screen.findByText(/Hardcover/);

    expect(screen.getByRole("button", { name: /start search/i })).toBeDisabled();
  });
});
