import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { DiffDisplay, TagMismatchDiffDisplay } from "./DiffDisplay";

describe("DiffDisplay", () => {
  it("renders identical text without addition/removal classes", () => {
    const { container } = render(<DiffDisplay original="Brandon" modified="Brandon" />);
    expect(screen.getByText("Brandon")).toBeInTheDocument();
    expect(container.querySelector(".text-emerald-600")).toBeNull();
    expect(container.querySelector(".text-rose-600")).toBeNull();
  });

  it("highlights added and removed characters", () => {
    const { container } = render(<DiffDisplay actual="Bran" expected="Brandon" />);
    expect(container.querySelector(".text-emerald-600")).toBeInTheDocument();
    expect(screen.getByText("don")).toBeInTheDocument();
  });

  it("renders per-field diffs from the JSON payload without mistaking value text for labels", () => {
    // Regression: the detector stores a JSON array of field records, because a "Field: value"
    // line format is ambiguous for free-text values. A description containing a literal
    // "Publisher: " line must render as Description's value, never as a field boundary.
    const descriptionText = "A gripping tale.\nPublisher: reprinted by example press\nRead it now.";
    const { container } = render(
      <TagMismatchDiffDisplay
        expected={JSON.stringify([
          { field: "Description", value: "" },
          { field: "Publisher", value: "Head of Zeus" },
        ])}
        actual={JSON.stringify([
          { field: "Description", value: descriptionText },
          { field: "Publisher", value: "Macmillan Audio" },
        ])}
      />,
    );

    expect(screen.getByText("Description")).toBeInTheDocument();
    expect(screen.getByText("Publisher")).toBeInTheDocument();
    expect(container.querySelectorAll(".bg-muted\\/50")).toHaveLength(2);
    expect(screen.getByText(/reprinted by example press/)).toBeInTheDocument();
    expect(screen.queryByText(/reprinted by example press.*Read it now.*Macmillan/)).toBeNull();
  });

  it("falls back to a single whole-value diff when the payload is not JSON", () => {
    // Guard for the JSON-only contract: a value that is not a JSON field-record array is never
    // split on "Field: " marker lines - it renders as one whole-value diff instead.
    const { container } = render(
      <TagMismatchDiffDisplay
        expected="Publisher: Head of Zeus\nRating: 4.5"
        actual="Publisher: Macmillan Audio\nRating: 4.4"
      />,
    );

    expect(container.querySelectorAll(".bg-muted\\/50")).toHaveLength(1);
    expect(screen.queryByText("Publisher")).toBeNull();
    expect(screen.queryByText("Rating")).toBeNull();
  });
});
