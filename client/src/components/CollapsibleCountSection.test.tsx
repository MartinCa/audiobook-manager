import { describe, it, expect } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { CollapsibleCountSection } from "./CollapsibleCountSection";

describe("CollapsibleCountSection", () => {
  it("renders the label and count in the header and stays collapsed by default", () => {
    render(
      <CollapsibleCountSection label="Missing Books" count={3}>
        <p>Row content</p>
      </CollapsibleCountSection>,
    );

    expect(screen.getByRole("button", { name: /Missing Books \(3\)/ })).toBeInTheDocument();
    expect(screen.queryByText("Row content")).not.toBeInTheDocument();
  });

  it("expands the body when the header is clicked", () => {
    render(
      <CollapsibleCountSection label="Upcoming Books" count={2}>
        <p>Row content</p>
      </CollapsibleCountSection>,
    );

    fireEvent.click(screen.getByRole("button", { name: /Upcoming Books \(2\)/ }));

    expect(screen.getByText("Row content")).toBeInTheDocument();
  });

  it("renders open by default when defaultOpen is set", () => {
    render(
      <CollapsibleCountSection label="Upcoming Books" count={1} defaultOpen>
        <p>Row content</p>
      </CollapsibleCountSection>,
    );

    expect(screen.getByText("Row content")).toBeInTheDocument();
  });

  it("collapses an already-open section on a second click", () => {
    render(
      <CollapsibleCountSection label="Missing Books" count={1} defaultOpen>
        <p>Row content</p>
      </CollapsibleCountSection>,
    );

    fireEvent.click(screen.getByRole("button", { name: /Missing Books \(1\)/ }));

    expect(screen.queryByText("Row content")).not.toBeInTheDocument();
  });
});
