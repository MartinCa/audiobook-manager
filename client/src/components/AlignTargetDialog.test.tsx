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

    expect(onConfirm).toHaveBeenCalledWith("Brandon Sanderson", [
      "Brandon Sanderson",
      "brandon sanderson",
    ]);
    expect(onOpenChange).toHaveBeenCalledWith(false);
    // onOpenChange is mocked, so `open` stays true and the dialog stays mounted - confirming the
    // internal step actually reset back to "select" rather than staying on "confirm".
    expect(screen.getByText("Select Target Alignment Value")).toBeInTheDocument();
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

  it("unchecking a candidate excludes it from the confirm payload and the affected-book count", () => {
    const threeCandidates: SimilarValueCandidate[] = [
      { value: "Brandon Sanderson", bookCount: 3 },
      { value: "brandon sanderson", bookCount: 1 },
      { value: "Brandon  Sanderson", bookCount: 2 },
    ];
    const onConfirm = vi.fn();
    render(
      <AlignTargetDialog
        open={true}
        onOpenChange={vi.fn()}
        candidates={threeCandidates}
        valueType="author"
        onConfirm={onConfirm}
      />,
    );

    // Uncheck "Brandon  Sanderson" (the double-spaced candidate, 2 books) - the target stays
    // "Brandon Sanderson" (default) and "brandon sanderson" (1 book) stays checked, so the
    // alignment would still change something.
    const checkboxes = screen.getAllByRole("checkbox");
    fireEvent.click(checkboxes[2]!);

    fireEvent.click(screen.getByText("Continue"));
    // The unchecked candidate's books must not be counted as affected any more.
    expect(screen.getByText(/1 book/)).toBeInTheDocument();
    expect(screen.queryByText(/2 books/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByText("Apply"));
    expect(onConfirm).toHaveBeenCalledWith("Brandon Sanderson", [
      "Brandon Sanderson",
      "brandon sanderson",
    ]);
  });

  it("unchecking the current radio target reassigns it to another checked candidate", () => {
    render(
      <AlignTargetDialog
        open={true}
        onOpenChange={vi.fn()}
        candidates={candidates}
        valueType="author"
        onConfirm={vi.fn()}
      />,
    );

    // The default target is the first candidate ("Brandon Sanderson"); uncheck it.
    const checkboxes = screen.getAllByRole("checkbox");
    fireEvent.click(checkboxes[0]!);

    const radios = screen.getAllByRole("radio");
    // The remaining checked candidate's radio must now be selected.
    expect(radios[1]).toHaveAttribute("aria-checked", "true");
  });

  it("unchecking every candidate but the custom target falls back to custom", () => {
    render(
      <AlignTargetDialog
        open={true}
        onOpenChange={vi.fn()}
        candidates={candidates}
        valueType="author"
        onConfirm={vi.fn()}
      />,
    );

    const checkboxes = screen.getAllByRole("checkbox");
    fireEvent.click(checkboxes[0]!);
    fireEvent.click(checkboxes[1]!);

    const radios = screen.getAllByRole("radio");
    expect(radios[2]).toHaveAttribute("aria-checked", "true"); // the "custom" radio
  });

  it("Continue is disabled once nothing checked would actually change", () => {
    render(
      <AlignTargetDialog
        open={true}
        onOpenChange={vi.fn()}
        candidates={candidates}
        valueType="author"
        onConfirm={vi.fn()}
      />,
    );

    // Target is "Brandon Sanderson"; uncheck the only other (differing) candidate so nothing
    // checked would change under this target.
    const checkboxes = screen.getAllByRole("checkbox");
    fireEvent.click(checkboxes[1]!);

    expect(screen.getByText("Continue").closest("button")).toBeDisabled();
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
