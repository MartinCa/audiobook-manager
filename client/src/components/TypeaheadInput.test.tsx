import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { useState } from "react";
import { TypeaheadInput } from "./TypeaheadInput";

function ControlledTypeahead({
  initialValue = "",
  candidates = [],
  multiValue = false,
  onSelectSuggestion,
}: {
  initialValue?: string;
  candidates?: string[];
  multiValue?: boolean;
  onSelectSuggestion?: (suggestion: string) => void;
}) {
  const [value, setValue] = useState(initialValue);
  return (
    <TypeaheadInput
      value={value}
      onValueChange={setValue}
      candidates={candidates}
      multiValue={multiValue}
      onSelectSuggestion={onSelectSuggestion}
      placeholder="Type here..."
    />
  );
}

describe("TypeaheadInput", () => {
  const authorCandidates = [
    "Brandon Sanderson",
    "Robert Jordan",
    "Neil Gaiman",
    "René Goscinny",
    "Stephen King",
  ];

  it("renders input with placeholder and value", () => {
    render(<ControlledTypeahead initialValue="Brandon" candidates={authorCandidates} />);
    const input = screen.getByPlaceholderText("Type here...");
    expect(input).toHaveValue("Brandon");
  });

  it("displays suggestions when typing a matching query in single-value mode", () => {
    render(<ControlledTypeahead candidates={authorCandidates} />);
    const input = screen.getByPlaceholderText("Type here...");

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "Sand" } });

    expect(screen.getByRole("listbox")).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Brandon Sanderson" })).toBeInTheDocument();
  });

  it("selects suggestion on pointerDown and updates input value in single-value mode", () => {
    const onSelect = vi.fn();
    render(<ControlledTypeahead candidates={authorCandidates} onSelectSuggestion={onSelect} />);
    const input = screen.getByPlaceholderText("Type here...");

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "Gaiman" } });

    const option = screen.getByRole("option", { name: "Neil Gaiman" });
    fireEvent.pointerDown(option);

    expect(input).toHaveValue("Neil Gaiman");
    expect(onSelect).toHaveBeenCalledWith("Neil Gaiman");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("handles multi-value comma-delimited input and completes only the active token", () => {
    render(<ControlledTypeahead candidates={authorCandidates} multiValue={true} />);
    const input = screen.getByPlaceholderText("Type here...");

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "Robert Jordan, Brand" } });

    expect(screen.getByRole("listbox")).toBeInTheDocument();
    const option = screen.getByRole("option", { name: "Brandon Sanderson" });
    expect(option).toBeInTheDocument();

    fireEvent.pointerDown(option);
    expect(input).toHaveValue("Robert Jordan, Brandon Sanderson");
  });

  it("supports keyboard navigation with ArrowDown, ArrowUp, and Enter", () => {
    render(<ControlledTypeahead candidates={authorCandidates} />);
    const input = screen.getByPlaceholderText("Type here...");

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "Step" } });

    const listbox = screen.getByRole("listbox");
    expect(listbox).toBeInTheDocument();

    // Arrow down to highlight
    fireEvent.keyDown(input, { key: "ArrowDown" });
    const option = screen.getByRole("option", { name: "Stephen King" });
    expect(option).toHaveAttribute("aria-selected", "true");

    // Enter to select
    fireEvent.keyDown(input, { key: "Enter" });
    expect(input).toHaveValue("Stephen King");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("closes dropdown on Escape key", () => {
    render(<ControlledTypeahead candidates={authorCandidates} />);
    const input = screen.getByPlaceholderText("Type here...");

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "Rob" } });
    expect(screen.getByRole("listbox")).toBeInTheDocument();

    fireEvent.keyDown(input, { key: "Escape" });
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("matches accent-insensitively when typing unaccented queries", () => {
    render(<ControlledTypeahead candidates={authorCandidates} />);
    const input = screen.getByPlaceholderText("Type here...");

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "Rene" } });

    expect(screen.getByRole("option", { name: "René Goscinny" })).toBeInTheDocument();
  });
});
