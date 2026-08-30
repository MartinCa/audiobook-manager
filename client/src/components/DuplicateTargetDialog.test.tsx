import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { DuplicateTargetDialog } from "./DuplicateTargetDialog";

describe("DuplicateTargetDialog", () => {
  it("renders new and existing file paths and triggers callbacks", () => {
    const onReplaceExisting = vi.fn();
    const onDeleteNew = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <DuplicateTargetDialog
        open={true}
        onOpenChange={onOpenChange}
        newPath="/staging/audiobook.m4b"
        newSizeInBytes={50000000}
        targetPath="/library/Author/audiobook.m4b"
        existingSizeInBytes={60000000}
        onReplaceExisting={onReplaceExisting}
        onDeleteNew={onDeleteNew}
      />,
    );

    expect(screen.getByText("/staging/audiobook.m4b")).toBeInTheDocument();
    expect(screen.getByText("/library/Author/audiobook.m4b")).toBeInTheDocument();

    const replaceBtn = screen.getByText("Replace existing");
    fireEvent.click(replaceBtn);
    expect(onReplaceExisting).toHaveBeenCalledTimes(1);

    const deleteBtn = screen.getByText("Delete new file");
    fireEvent.click(deleteBtn);
    expect(onDeleteNew).toHaveBeenCalledTimes(1);
  });
});
