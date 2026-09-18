import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { ConfirmDialog } from "./ConfirmDialog";

describe("ConfirmDialog", () => {
  it("calls onConfirm and closes on success", async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined);
    const onOpenChange = vi.fn();

    render(
      <ConfirmDialog
        open={true}
        onOpenChange={onOpenChange}
        title="Delete item"
        onConfirm={onConfirm}
        confirmText="Delete"
      >
        <p>Are you sure?</p>
      </ConfirmDialog>,
    );

    fireEvent.click(screen.getByText("Delete"));

    await waitFor(() => {
      expect(onConfirm).toHaveBeenCalledTimes(1);
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it("shows the pending state and disables both buttons while onConfirm is in flight", async () => {
    let resolveConfirm: () => void = () => {};
    const onConfirm = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          resolveConfirm = resolve;
        }),
    );

    render(
      <ConfirmDialog
        open={true}
        onOpenChange={vi.fn()}
        title="Delete item"
        onConfirm={onConfirm}
        confirmText="Delete"
        pendingText="Deleting..."
      >
        <p>Are you sure?</p>
      </ConfirmDialog>,
    );

    fireEvent.click(screen.getByText("Delete"));

    await waitFor(() => {
      expect(screen.getByText("Deleting...")).toBeInTheDocument();
    });
    expect(screen.getByText("Deleting...").closest("button")).toBeDisabled();
    expect(screen.getByText("Cancel").closest("button")).toBeDisabled();

    resolveConfirm();
    await waitFor(() => {
      expect(screen.queryByText("Deleting...")).not.toBeInTheDocument();
    });
  });

  it("stays open and clears pending when onConfirm rejects", async () => {
    const onConfirm = vi.fn().mockRejectedValue(new Error("boom"));
    const onOpenChange = vi.fn();

    render(
      <ConfirmDialog
        open={true}
        onOpenChange={onOpenChange}
        title="Delete item"
        onConfirm={onConfirm}
        confirmText="Delete"
      >
        <p>Are you sure?</p>
      </ConfirmDialog>,
    );

    fireEvent.click(screen.getByText("Delete"));

    await waitFor(() => {
      expect(onConfirm).toHaveBeenCalledTimes(1);
    });
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    expect(screen.getByText("Delete").closest("button")).not.toBeDisabled();
  });

  it("calls onOpenChange(false) on cancel without invoking onConfirm", () => {
    const onConfirm = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <ConfirmDialog
        open={true}
        onOpenChange={onOpenChange}
        title="Delete item"
        onConfirm={onConfirm}
      >
        <p>Are you sure?</p>
      </ConfirmDialog>,
    );

    fireEvent.click(screen.getByText("Cancel"));

    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it("disables the confirm button when confirmDisabled is set", () => {
    render(
      <ConfirmDialog
        open={true}
        onOpenChange={vi.fn()}
        title="Delete item"
        onConfirm={vi.fn()}
        confirmText="Delete"
        confirmDisabled
      >
        <p>Are you sure?</p>
      </ConfirmDialog>,
    );

    expect(screen.getByText("Delete").closest("button")).toBeDisabled();
  });
});
