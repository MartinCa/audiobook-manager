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
