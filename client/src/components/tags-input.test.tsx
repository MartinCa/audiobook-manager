import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { useState } from "react";
import { TagsInput, type TagsInputProps } from "./tags-input";

type ControlledTagsInputProps = {
  initial?: string[];
  onValueChange?: (value: string[]) => void;
} & Partial<Omit<TagsInputProps, "value" | "onValueChange">>;

function ControlledTagsInput({
  initial = [],
  onValueChange,
  placeholder = "Fantasy, Fiction",
  ...rest
}: ControlledTagsInputProps) {
  const [value, setValue] = useState(initial);
  return (
    <TagsInput
      value={value}
      onValueChange={(v) => {
        setValue(v);
        onValueChange?.(v);
      }}
      placeholder={placeholder}
      {...rest}
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

  describe("editing an existing chip", () => {
    it("clicking a chip turns it into an editable input pre-filled with its value", () => {
      render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} />);

      fireEvent.click(screen.getByLabelText("Edit Fantasy"));

      expect(screen.getByDisplayValue("Fantasy")).toBeInTheDocument();
    });

    it("commits an edit in place on Enter, preserving the order of every other entry", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput initial={["Alpha", "Beta", "Gamma"]} onValueChange={onValueChange} />,
      );

      fireEvent.click(screen.getByLabelText("Edit Beta"));
      const editInput = screen.getByDisplayValue("Beta");
      fireEvent.change(editInput, { target: { value: "Beta Fixed" } });
      fireEvent.keyDown(editInput, { key: "Enter" });

      expect(onValueChange).toHaveBeenCalledWith(["Alpha", "Beta Fixed", "Gamma"]);
    });

    it("commits an edit on blur too, not just Enter", () => {
      const onValueChange = vi.fn();
      render(<ControlledTagsInput initial={["Fantasy"]} onValueChange={onValueChange} />);

      fireEvent.click(screen.getByLabelText("Edit Fantasy"));
      const editInput = screen.getByDisplayValue("Fantasy");
      fireEvent.change(editInput, { target: { value: "Fantasy Fixed" } });
      fireEvent.blur(editInput);

      expect(onValueChange).toHaveBeenCalledWith(["Fantasy Fixed"]);
    });

    it("cancels the edit on Escape without changing the value", () => {
      const onValueChange = vi.fn();
      render(<ControlledTagsInput initial={["Fantasy"]} onValueChange={onValueChange} />);

      fireEvent.click(screen.getByLabelText("Edit Fantasy"));
      const editInput = screen.getByDisplayValue("Fantasy");
      fireEvent.change(editInput, { target: { value: "Something Else" } });
      fireEvent.keyDown(editInput, { key: "Escape" });

      expect(onValueChange).not.toHaveBeenCalled();
      expect(screen.getByText("Fantasy")).toBeInTheDocument();
      expect(screen.queryByDisplayValue("Something Else")).not.toBeInTheDocument();
    });

    it("removes the entry when edited down to an empty value", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput initial={["Fantasy", "Fiction"]} onValueChange={onValueChange} />,
      );

      fireEvent.click(screen.getByLabelText("Edit Fantasy"));
      const editInput = screen.getByDisplayValue("Fantasy");
      fireEvent.change(editInput, { target: { value: "   " } });
      fireEvent.keyDown(editInput, { key: "Enter" });

      expect(onValueChange).toHaveBeenCalledWith(["Fiction"]);
    });

    it("rejects an edit that would duplicate another existing entry", () => {
      const onValueChange = vi.fn();
      render(
        <ControlledTagsInput initial={["Fantasy", "Fiction"]} onValueChange={onValueChange} />,
      );

      fireEvent.click(screen.getByLabelText("Edit Fantasy"));
      const editInput = screen.getByDisplayValue("Fantasy");
      fireEvent.change(editInput, { target: { value: "fiction" } });
      fireEvent.keyDown(editInput, { key: "Enter" });

      expect(onValueChange).not.toHaveBeenCalled();
      expect(screen.getByText("Fantasy")).toBeInTheDocument();
    });
  });

  describe("typeahead suggestions", () => {
    const authorNames = ["Brandon Sanderson", "Brandon Ellis", "Frank Herbert"];

    it("shows narrowed suggestions while typing a new entry", () => {
      render(<ControlledTagsInput suggestions={authorNames} />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "Brandon" } });

      expect(screen.getByText("Brandon Sanderson")).toBeInTheDocument();
      expect(screen.getByText("Brandon Ellis")).toBeInTheDocument();
      expect(screen.queryByText("Frank Herbert")).not.toBeInTheDocument();
    });

    it("does not suggest a candidate that is already a committed chip", () => {
      render(<ControlledTagsInput initial={["Brandon Sanderson"]} suggestions={authorNames} />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "Brandon" } });

      expect(screen.queryByRole("option", { name: "Brandon Sanderson" })).not.toBeInTheDocument();
      expect(screen.getByRole("option", { name: "Brandon Ellis" })).toBeInTheDocument();
    });

    it("clicking a suggestion commits it as a chip and clears the draft", () => {
      const onValueChange = vi.fn();
      render(<ControlledTagsInput suggestions={authorNames} onValueChange={onValueChange} />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "Frank" } });
      fireEvent.pointerDown(screen.getByText("Frank Herbert"));

      expect(onValueChange).toHaveBeenCalledWith(["Frank Herbert"]);
      expect(input).toHaveValue("");
    });

    it("selecting a highlighted suggestion with Enter commits it", () => {
      const onValueChange = vi.fn();
      render(<ControlledTagsInput suggestions={authorNames} onValueChange={onValueChange} />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "Brandon" } });
      fireEvent.keyDown(input, { key: "ArrowDown" });
      fireEvent.keyDown(input, { key: "Enter" });

      expect(onValueChange).toHaveBeenCalledWith(["Brandon Sanderson"]);
    });

    it("does not show suggestions when none are configured (Genres usage)", () => {
      render(<ControlledTagsInput />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "Fantasy" } });

      expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    });
  });

  describe("onEntryCommitted", () => {
    it("fires with the newly committed value", () => {
      const onEntryCommitted = vi.fn();
      render(<ControlledTagsInput onEntryCommitted={onEntryCommitted} />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "Heartfelt" } });
      fireEvent.keyDown(input, { key: "Enter" });

      expect(onEntryCommitted).toHaveBeenCalledWith("Heartfelt");
    });

    it("does not fire when the entry is rejected as a duplicate", () => {
      const onEntryCommitted = vi.fn();
      render(<ControlledTagsInput initial={["Fantasy"]} onEntryCommitted={onEntryCommitted} />);

      const input = screen.getByRole("textbox");
      fireEvent.change(input, { target: { value: "fantasy" } });
      fireEvent.keyDown(input, { key: "Enter" });

      expect(onEntryCommitted).not.toHaveBeenCalled();
    });
  });

  describe("reordering", () => {
    it("does not render a drag handle when reorderable is not set (Genres usage)", () => {
      render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} />);

      expect(screen.queryByLabelText("Reorder Fantasy")).not.toBeInTheDocument();
    });

    it("renders a drag handle per chip when reorderable", () => {
      render(<ControlledTagsInput initial={["Fantasy", "Fiction"]} reorderable />);

      expect(screen.getByLabelText("Reorder Fantasy")).toBeInTheDocument();
      expect(screen.getByLabelText("Reorder Fiction")).toBeInTheDocument();
    });

    it("hides the drag handle when disabled even if reorderable", () => {
      render(<ControlledTagsInput initial={["Fantasy"]} reorderable disabled />);

      expect(screen.queryByLabelText("Reorder Fantasy")).not.toBeInTheDocument();
    });
  });
});
