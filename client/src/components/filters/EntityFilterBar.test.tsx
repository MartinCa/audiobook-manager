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

const MULTISELECT_FIELDS: FilterFieldDef[] = [
  {
    type: "multiselect",
    key: "languages",
    label: "Language",
    options: ["en", "da", "unrecognized"],
    // "unrecognized" deliberately has no entry - covers the raw-value fallback (e.g. a language
    // the backend's GET /api/settings/languages doesn't know about).
    optionLabels: { en: "English", da: "Danish" },
  },
];

// Mirrors the "Matched source" field every list page defines (Bug 6): a synthetic "Unsupported"
// value relabeled to "Unsupported/None", plus an "Any supported" convenience that expands to
// every real option.
const SOURCES_FIELDS: FilterFieldDef[] = [
  {
    type: "multiselect",
    key: "sources",
    label: "Matched source",
    options: ["Audible", "Hardcover", "Unsupported"],
    optionLabels: { Unsupported: "Unsupported/None" },
    selectAllOption: { label: "Any supported", excludeValues: ["Unsupported"] },
  },
];

const DURATION_FIELDS: FilterFieldDef[] = [
  {
    type: "numberRange",
    label: "Duration (minutes)",
    minKey: "minDurationInSeconds",
    maxKey: "maxDurationInSeconds",
    unit: {
      toDisplay: (storedSeconds: number) => Math.round(storedSeconds / 60),
      toStored: (displayMinutes: number) => displayMinutes * 60,
    },
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

  it("renders multiselect options using optionLabels, falling back to the raw value when absent", () => {
    render(<EntityFilterBar fields={MULTISELECT_FIELDS} values={{}} onChange={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Any" }));

    expect(screen.getByText("English")).toBeInTheDocument();
    expect(screen.getByText("Danish")).toBeInTheDocument();
    expect(screen.getByText("unrecognized")).toBeInTheDocument();
  });

  it("selecting a multiselect option reports the raw value, not its display label", () => {
    const onChange = vi.fn();
    render(<EntityFilterBar fields={MULTISELECT_FIELDS} values={{}} onChange={onChange} />);

    fireEvent.click(screen.getByRole("button", { name: "Any" }));
    fireEvent.click(screen.getByText("English"));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ languages: ["en"] }));
  });

  it("shows the display label, not the raw value, in the trigger and the active-filter chip", () => {
    render(
      <EntityFilterBar
        fields={MULTISELECT_FIELDS}
        values={{ languages: ["en"] }}
        onChange={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: "English" })).toBeInTheDocument();
    expect(screen.getByText("Language: English")).toBeInTheDocument();
  });

  // Bug 6: the "Unsupported" value displays as "Unsupported/None" via optionLabels (already
  // covered above for a plain language field's fallback) and gains an "Any supported" convenience
  // item above the real options.
  it("renders the Unsupported/None label and an Any supported item for a sources field", () => {
    render(<EntityFilterBar fields={SOURCES_FIELDS} values={{}} onChange={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Any" }));

    expect(screen.getByText("Any supported")).toBeInTheDocument();
    expect(screen.getByText("Unsupported/None")).toBeInTheDocument();
    expect(screen.getByText("Audible")).toBeInTheDocument();
  });

  it("Any supported selects every real option except the excluded Unsupported/None bucket", () => {
    const onChange = vi.fn();
    render(<EntityFilterBar fields={SOURCES_FIELDS} values={{}} onChange={onChange} />);

    fireEvent.click(screen.getByRole("button", { name: "Any" }));
    fireEvent.click(screen.getByText("Any supported"));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ sources: ["Audible", "Hardcover"] }),
    );
  });

  it("Any supported is checked and shown as the trigger label when every real option is selected", () => {
    render(
      <EntityFilterBar
        fields={SOURCES_FIELDS}
        values={{ sources: ["Audible", "Hardcover"] }}
        onChange={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: "Any supported" })).toBeInTheDocument();
  });

  it("toggling Any supported off again clears the selection", () => {
    const onChange = vi.fn();
    render(
      <EntityFilterBar
        fields={SOURCES_FIELDS}
        values={{ sources: ["Audible", "Hardcover"] }}
        onChange={onChange}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Any supported" }));
    fireEvent.click(screen.getByRole("menuitemcheckbox", { name: "Any supported" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ sources: undefined }));
  });

  // Bug 7: a numberRange field's optional `unit` converts the stored (filter/URL) value to what
  // the input displays, and converts a typed value back before it lands in filter/URL state.
  it("displays a stored duration in seconds converted to minutes in the input", () => {
    render(
      <EntityFilterBar
        fields={DURATION_FIELDS}
        values={{ minDurationInSeconds: 600 }}
        onChange={vi.fn()}
      />,
    );

    expect(screen.getByLabelText("Duration (minutes) minimum")).toHaveValue(10);
  });

  it("converts a typed minutes value to seconds before reporting the change", () => {
    const onChange = vi.fn();
    render(<EntityFilterBar fields={DURATION_FIELDS} values={{}} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Duration (minutes) minimum"), {
      target: { value: "10" },
    });

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ minDurationInSeconds: 600 }));
  });

  it("converts a fractional typed minutes value to seconds without flooring it first", () => {
    // Regression: toIntFilterValue used to truncate the raw parsed value BEFORE applying the
    // unit conversion, so 2.5 minutes floored to 2 minutes and only then converted to 120s -
    // silently dropping the 30s. Converting first preserves it: 2.5 min -> 150s.
    const onChange = vi.fn();
    render(<EntityFilterBar fields={DURATION_FIELDS} values={{}} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Duration (minutes) minimum"), {
      target: { value: "2.5" },
    });

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ minDurationInSeconds: 150 }));
  });

  it("shows a stored duration range chip converted to minutes, not raw seconds", () => {
    render(
      <EntityFilterBar
        fields={DURATION_FIELDS}
        values={{ minDurationInSeconds: 600, maxDurationInSeconds: 1800 }}
        onChange={vi.fn()}
      />,
    );

    expect(screen.getByText("Duration (minutes): 10–30")).toBeInTheDocument();
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
