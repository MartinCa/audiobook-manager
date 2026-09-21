import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { EntryStatusHint } from "./EntryStatusHint";
import type { EntryStatus } from "@/types/EntryStatus";

function makeStatus(overrides: Partial<EntryStatus> = {}): EntryStatus {
  return {
    value: "Jane Author",
    status: "new",
    exactMatch: null,
    similarMatches: [],
    ...overrides,
  };
}

describe("EntryStatusHint", () => {
  it("renders nothing for a null (loading/blank) status", () => {
    const { container } = render(<EntryStatusHint status={null} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing for a blank value", () => {
    const { container } = render(<EntryStatusHint status={makeStatus({ value: "  " })} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("marks an exact existing match by name", () => {
    render(
      <EntryStatusHint
        status={makeStatus({
          value: "René",
          status: "exact",
          exactMatch: { id: 5, name: "René" },
        })}
      />,
    );

    expect(screen.getByText("René")).toBeInTheDocument();
    expect(screen.getByText("— existing entry")).toBeInTheDocument();
  });

  it("flags a casing-only mismatch as an actionable warning instead of a plain match", () => {
    const onUseMatch = vi.fn();
    render(
      <EntryStatusHint
        status={makeStatus({
          value: "the murderbot diaries",
          status: "exact",
          exactMatch: { id: null, name: "The Murderbot Diaries" },
        })}
        onUseMatch={onUseMatch}
      />,
    );

    const hint = screen.getByRole("button", { name: /different casing.*click to fix casing/i });
    fireEvent.click(hint);
    expect(onUseMatch).toHaveBeenCalledWith("The Murderbot Diaries");
    expect(screen.queryByText("— existing entry")).not.toBeInTheDocument();
  });

  it("shows a casing mismatch as informational text when no onUseMatch is given", () => {
    render(
      <EntryStatusHint
        status={makeStatus({
          value: "jane author",
          status: "exact",
          exactMatch: { id: 5, name: "Jane Author" },
        })}
      />,
    );

    expect(screen.getByText(/different casing.*Jane Author/i)).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  // The exact match is case *and accent* insensitive (see EntryValueStatus), so "exact" also
  // covers an accent-only difference (e.g. "rene" vs "René"). That must not be mislabeled as a
  // casing difference, since "click to fix casing" would be wrong about what the click changes.
  it("flags an accent-only mismatch as a spelling difference, not a casing one", () => {
    const onUseMatch = vi.fn();
    render(
      <EntryStatusHint
        status={makeStatus({
          value: "rene",
          status: "exact",
          exactMatch: { id: 5, name: "René" },
        })}
        onUseMatch={onUseMatch}
      />,
    );

    const hint = screen.getByRole("button", { name: /spelled differently.*click to use it/i });
    fireEvent.click(hint);
    expect(onUseMatch).toHaveBeenCalledWith("René");
    expect(screen.queryByText(/different casing/i)).not.toBeInTheDocument();
  });

  it("offers the similar candidate as a click-to-use hint", () => {
    const onUseMatch = vi.fn();
    render(
      <EntryStatusHint
        status={makeStatus({
          value: "Jane Authorr",
          status: "similar",
          similarMatches: [{ id: 1, name: "Jane Author" }],
        })}
        onUseMatch={onUseMatch}
      />,
    );

    const hint = screen.getByRole("button", { name: /Similar to "Jane Author" — click to use/i });
    fireEvent.click(hint);
    expect(onUseMatch).toHaveBeenCalledWith("Jane Author");
  });

  it("counts multiple similar candidates", () => {
    render(
      <EntryStatusHint
        status={makeStatus({
          value: "Stormlight",
          status: "similar",
          similarMatches: [{ id: null, name: "The Stormlight Archive" }],
        })}
      />,
    );

    expect(screen.getByText(/Similar to "The Stormlight Archive"/)).toBeInTheDocument();
  });

  it("marks an unknown value as new", () => {
    render(<EntryStatusHint status={makeStatus({ value: "Nova Writer" })} />);

    expect(screen.getByText(/New — no exact or similar entry in the library/i)).toBeInTheDocument();
  });

  it("renders an explicit error note instead of a silent guess when the classification query failed", () => {
    render(<EntryStatusHint status={null} isError />);

    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(
      screen.getByText(/couldn't check the library for existing entries/i),
    ).toBeInTheDocument();
    expect(screen.queryByText(/New — no exact or similar entry/i)).not.toBeInTheDocument();
  });
});
