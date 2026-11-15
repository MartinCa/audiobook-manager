import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { SharedValueChoice } from "./SharedValueChoice";

function renderChoice(props: Partial<React.ComponentProps<typeof SharedValueChoice>> = {}) {
  const onChange = vi.fn();
  render(
    <SharedValueChoice
      values={["A One", "B Two"]}
      value={undefined}
      onChange={onChange}
      emptyText="Nothing shared."
      legend="Pick one"
      idPrefix="t"
      {...props}
    />,
  );
  return onChange;
}

describe("SharedValueChoice", () => {
  it("offers every shared value plus Leave unchanged, which is selected when nothing is picked", () => {
    renderChoice();

    expect(screen.getByRole("radio", { name: "Leave unchanged" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "A One" })).not.toBeChecked();
    expect(screen.getByRole("radio", { name: "B Two" })).not.toBeChecked();
  });

  it("checks the picked value", () => {
    renderChoice({ value: "B Two" });

    expect(screen.getByRole("radio", { name: "B Two" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Leave unchanged" })).not.toBeChecked();
  });

  it("reports a picked value by name, and Leave unchanged as undefined - never the internal sentinel", () => {
    const onChange = renderChoice({ value: "A One" });

    fireEvent.click(screen.getByRole("radio", { name: "B Two" }));
    expect(onChange).toHaveBeenLastCalledWith("B Two");
    fireEvent.click(screen.getByRole("radio", { name: "Leave unchanged" }));
    expect(onChange).toHaveBeenLastCalledWith(undefined);
  });

  it("says so, with no radios, when nothing is shared", () => {
    renderChoice({ values: [] });

    expect(screen.getByText("Nothing shared.")).toBeInTheDocument();
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
  });

  it("shows a warning as an alert only when given one", () => {
    renderChoice({ warning: "That drops them." });

    expect(screen.getByRole("alert")).toHaveTextContent("That drops them.");
  });

  it("shows no alert without a warning", () => {
    renderChoice();

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
