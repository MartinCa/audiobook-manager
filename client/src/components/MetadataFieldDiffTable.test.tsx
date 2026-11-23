import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { MetadataFieldDiffTable } from "@/components/MetadataFieldDiffTable";
import type { FieldDiff } from "@/hooks/useMetadataFieldDiffs";

const field = (key: string, changed: boolean): FieldDiff => ({
  key,
  label: key.toUpperCase(),
  currentValue: "a",
  newValue: changed ? "b" : "a",
  changed,
});

function renderTable(fields: FieldDiff[]) {
  const props = {
    selected: new Set<string>(),
    onToggleField: () => {},
    onToggleAll: () => {},
    changedFieldKeys: fields.filter((f) => f.changed).map((f) => f.key),
  };
  const ui = <MetadataFieldDiffTable fields={fields} {...props} />;
  const view = render(ui);
  return {
    rerenderWith: (next: FieldDiff[]) =>
      view.rerender(
        <MetadataFieldDiffTable
          fields={next}
          {...props}
          changedFieldKeys={next.filter((f) => f.changed).map((f) => f.key)}
        />,
      ),
  };
}

const labels = () =>
  screen
    .getAllByRole("row")
    .slice(1)
    .map((row) => within(row).getAllByRole("cell")[1]?.textContent);

describe("MetadataFieldDiffTable ordering", () => {
  it("lists changed rows first, keeping each group's order", () => {
    renderTable([field("a", false), field("b", true), field("c", false), field("d", true)]);
    expect(labels()).toEqual(["B", "D", "A", "C"]);
  });

  it("does not reorder rows when a row's changed flag flips", () => {
    const { rerenderWith } = renderTable([field("a", false), field("b", true), field("c", true)]);
    expect(labels()).toEqual(["B", "C", "A"]);
    rerenderWith([field("a", false), field("b", false), field("c", true)]);
    expect(labels()).toEqual(["B", "C", "A"]);
  });

  it("re-derives the order when the set of rows changes", () => {
    const { rerenderWith } = renderTable([field("a", false), field("b", true)]);
    expect(labels()).toEqual(["B", "A"]);
    rerenderWith([field("a", false), field("b", false), field("c", true)]);
    expect(labels()).toEqual(["C", "A", "B"]);
  });
});
