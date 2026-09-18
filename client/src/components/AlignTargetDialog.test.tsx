import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { AlignTargetDialog } from "./AlignTargetDialog";
import type { SimilarValueCandidate } from "@/types/SimilarValue";

const candidates: SimilarValueCandidate[] = [
  { value: "Brandon Sanderson", bookCount: 3 },
  { value: "brandon sanderson", bookCount: 1 },
];

describe("AlignTargetDialog", () => {
  it("walks select -> confirm -> onConfirm, then resets on the way out", () => {
    const onConfirm = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <AlignTargetDialog
        open={true}
        onOpenChange={onOpenChange}
        candidates={candidates}
        valueType="author"
        onConfirm={onConfirm}
      />,
    );

    expect(screen.getByText("Select Target Alignment Value")).toBeInTheDocument();
    fireEvent.click(screen.getByText("Continue"));

    expect(screen.getByText("Confirm Alignment")).toBeInTheDocument();
    expect(screen.getByText(/1 book/)).toBeInTheDocument();

    fireEvent.click(screen.getByText("Apply"));

    expect(onConfirm).toHaveBeenCalledWith("Brandon Sanderson");
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("disables Continue until a custom value is entered", () => {
    render(
      <AlignTargetDialog
        open={true}
        onOpenChange={vi.fn()}
        candidates={candidates}
        valueType="author"
        onConfirm={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByText("Custom value:"));
    expect(screen.getByText("Continue").closest("button")).toBeDisabled();

    fireEvent.change(screen.getByPlaceholderText("Enter custom value..."), {
      target: { value: "New Author" },
    });
    expect(screen.getByText("Continue").closest("button")).not.toBeDisabled();
  });

  it("Back returns to the select step without closing", () => {
    render(
      <AlignTargetDialog
        open={true}
        onOpenChange={vi.fn()}
        candidates={candidates}
        valueType="author"
        onConfirm={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByText("Continue"));
    expect(screen.getByText("Confirm Alignment")).toBeInTheDocument();

    fireEvent.click(screen.getByText("Back"));
    expect(screen.getByText("Select Target Alignment Value")).toBeInTheDocument();
  });

  it("Cancel resets the step and closes", () => {
    const onOpenChange = vi.fn();
    render(
      <AlignTargetDialog
        open={true}
        onOpenChange={onOpenChange}
        candidates={candidates}
        valueType="author"
        onConfirm={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByText("Continue"));
    fireEvent.click(screen.getByText("Back"));
    fireEvent.click(screen.getByText("Cancel"));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
