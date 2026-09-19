import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { EntityFilterBar, type FilterFieldDef, type FilterValueMap } from "./EntityFilterBar";

const FIELDS: FilterFieldDef[] = [
  {
    type: "tristate",
    key: "followed",
    label: "Followed",
    trueLabel: "Followed",
    falseLabel: "Not followed",
  },
  { type: "numberRange", label: "Owned books", minKey: "minOwnedBooks", maxKey: "maxOwnedBooks" },
  {
    type: "dateRange",
    label: "Last refreshed",
    afterKey: "refreshedAfter",
    beforeKey: "refreshedBefore",
    neverKey: "neverRefreshed",
    neverLabel: "Never refreshed",
  },
];

describe("EntityFilterBar", () => {
  it("renders no chips when no filter is set", () => {
    render(<EntityFilterBar fields={FIELDS} values={{}} onChange={vi.fn()} />);

    expect(screen.queryByText("Clear all")).not.toBeInTheDocument();
  });

  it("updates the min/max number range inputs and reports the change", () => {
    const onChange = vi.fn();
    render(<EntityFilterBar fields={FIELDS} values={{}} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Owned books minimum"), { target: { value: "3" } });

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ minOwnedBooks: 3 }));
  });

  // Regression: minOwnedBooks/maxOwnedBooks bind to an int? on the server, which rejects a
  // fraction or exponent notation with an unexplained 400. Number(e.target.value) used to pass
  // "2.5" straight through.
  it("truncates a fractional number input to an integer before reporting the change", () => {
    const onChange = vi.fn();
    render(<EntityFilterBar fields={FIELDS} values={{}} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Owned books minimum"), { target: { value: "2.5" } });

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ minOwnedBooks: 2 }));
  });

  it("clearing a number input reports undefined rather than an empty string", () => {
    const onChange = vi.fn();
    render(<EntityFilterBar fields={FIELDS} values={{ minOwnedBooks: 3 }} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Owned books minimum"), { target: { value: "" } });

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ minOwnedBooks: undefined }));
  });

  it("shows an active-filter chip summarizing a number range and clears it on click", () => {
    const onChange = vi.fn();
    render(
      <EntityFilterBar
        fields={FIELDS}
        values={{ minOwnedBooks: 2, maxOwnedBooks: 10 }}
        onChange={onChange}
      />,
    );

    expect(screen.getByText("Owned books: 2–10")).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText(/Remove filter: Owned books/));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ minOwnedBooks: undefined, maxOwnedBooks: undefined }),
    );
  });

  it("the never-refreshed toggle clears any date bounds and disables the date inputs", () => {
    const onChange = vi.fn();
    const { rerender } = render(
      <EntityFilterBar
        fields={FIELDS}
        values={{ refreshedAfter: "2024-01-01" }}
        onChange={onChange}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Never refreshed" }));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({
        neverRefreshed: true,
        refreshedAfter: undefined,
        refreshedBefore: undefined,
      }),
    );

    rerender(
      <EntityFilterBar fields={FIELDS} values={{ neverRefreshed: true }} onChange={onChange} />,
    );

    expect(screen.getByLabelText("Last refreshed after")).toBeDisabled();
    expect(screen.getByText("Last refreshed: Never refreshed")).toBeInTheDocument();
  });

  it("clear all resets every field to undefined", () => {
    const onChange = vi.fn();
    const values: FilterValueMap = {
      followed: true,
      minOwnedBooks: 2,
      refreshedAfter: "2024-01-01",
    };
    render(<EntityFilterBar fields={FIELDS} values={values} onChange={onChange} />);

    fireEvent.click(screen.getByRole("button", { name: "Clear all" }));

    expect(onChange).toHaveBeenCalledWith({
      followed: undefined,
      minOwnedBooks: undefined,
      refreshedAfter: undefined,
    });
  });
});
