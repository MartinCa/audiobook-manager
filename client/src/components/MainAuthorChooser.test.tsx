import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MainAuthorChooser } from "./MainAuthorChooser";

function renderChooser(props: Partial<React.ComponentProps<typeof MainAuthorChooser>> = {}) {
  const onChange = vi.fn();
  render(
    <MainAuthorChooser
      options={["A One", "B Two"]}
      value="B Two"
      onChange={onChange}
      idPrefix="t"
      {...props}
    />,
  );
  return onChange;
}

describe("MainAuthorChooser", () => {
  it("lists every source author plus a No main author choice, with the current value checked", () => {
    renderChooser();

    expect(screen.getByRole("radio", { name: "A One" })).not.toBeChecked();
    expect(screen.getByRole("radio", { name: "B Two" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "No main author" })).not.toBeChecked();
  });

  it("checks No main author when the value is empty", () => {
    renderChooser({ value: "" });

    expect(screen.getByRole("radio", { name: "No main author" })).toBeChecked();
  });

  it("reports a picked author by name", async () => {
    const onChange = renderChooser();

    await userEvent.setup().click(screen.getByRole("radio", { name: "A One" }));

    expect(onChange).toHaveBeenCalledWith("A One");
  });

  it("reports No main author as an empty string, never the internal sentinel", async () => {
    const onChange = renderChooser();

    await userEvent.setup().click(screen.getByRole("radio", { name: "No main author" }));

    expect(onChange).toHaveBeenCalledWith("");
  });

  it("shows the ask note only when given one", () => {
    renderChooser({ askNote: "Please confirm." });

    expect(screen.getByText("Please confirm.")).toBeInTheDocument();
  });
});
