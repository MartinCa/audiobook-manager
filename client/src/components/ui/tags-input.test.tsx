import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { useState } from "react";
import { TagsInput } from "./tags-input";

function ControlledTagsInput({
  initial = [],
  onValueChange,
}: {
  initial?: string[];
  onValueChange?: (value: string[]) => void;
}) {
  const [value, setValue] = useState(initial);
  return (
    <TagsInput
      value={value}
      onValueChange={(v) => {
        setValue(v);
        onValueChange?.(v);
      }}
      placeholder="Fantasy, Fiction"
    />
  );
}

describe("TagsInput", () => {
  it("renders existing values as chips", () => {
    render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} />);
    expect(screen.getByText("Fantasy")).toBeInTheDocument();
    expect(screen.getByText("Fiction")).toBeInTheDocument();
  });

  it("commits the typed text as a new chip on Enter and clears the draft", () => {
    const onValueChange = vi.fn();
    render(<ControlledTagsInput onValueChange={onValueChange} />);

    const input = screen.getByPlaceholderText("Fantasy, Fiction");
    fireEvent.change(input, { target: { value: "Heartfelt" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(onValueChange).toHaveBeenCalledWith(["Heartfelt"]);
    expect(screen.getByText("Heartfelt")).toBeInTheDocument();
    expect(input).toHaveValue("");
  });

  it("commits the draft on blur too, not just Enter", () => {
    render(<ControlledTagsInput />);

    const input = screen.getByPlaceholderText("Fantasy, Fiction");
    fireEvent.change(input, { target: { value: "Thought-Provoking" } });
    fireEvent.blur(input);

    expect(screen.getByText("Thought-Provoking")).toBeInTheDocument();
  });

  it("does not add a duplicate chip (case-insensitive)", () => {
    const onValueChange = vi.fn();
    render(<ControlledTagsInput initial={["Fantasy"]} onValueChange={onValueChange} />);

    const input = screen.getByRole("textbox");
    fireEvent.change(input, { target: { value: "fantasy" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(onValueChange).not.toHaveBeenCalled();
    expect(screen.getAllByText(/fantasy/i)).toHaveLength(1);
  });

  it("removes the last chip on Backspace when the draft is empty", () => {
    const onValueChange = vi.fn();
    render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} onValueChange={onValueChange} />);

    const input = screen.getByRole("textbox");
    fireEvent.keyDown(input, { key: "Backspace" });

    expect(onValueChange).toHaveBeenCalledWith(["Fantasy"]);
  });

  it("removes a chip via its remove button", () => {
    const onValueChange = vi.fn();
    render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} onValueChange={onValueChange} />);

    fireEvent.click(screen.getByLabelText("Remove Fantasy"));

    expect(onValueChange).toHaveBeenCalledWith(["Fiction"]);
  });

  it("ignores a Backspace on empty draft when there are no chips left", () => {
    const onValueChange = vi.fn();
    render(<ControlledTagsInput onValueChange={onValueChange} />);

    const input = screen.getByRole("textbox");
    fireEvent.keyDown(input, { key: "Backspace" });

    expect(onValueChange).not.toHaveBeenCalled();
  });
});
