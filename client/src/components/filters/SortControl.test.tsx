import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SortControl } from "./SortControl";
import { AUTHOR_SORT } from "@/helpers/listSort";

describe("SortControl", () => {
  it("shows the default key ascending when nothing is sorted", () => {
    render(<SortControl config={AUTHOR_SORT} sort={{}} onChange={vi.fn()} noun="authors" />);

    expect(screen.getByRole("combobox", { name: "Sort authors by" })).toHaveTextContent("Name");
    expect(
      screen.getByRole("button", { name: "Ascending - switch to descending" }),
    ).toBeInTheDocument();
  });

  it("shows the committed key and direction", () => {
    render(
      <SortControl
        config={AUTHOR_SORT}
        sort={{ sortBy: "bookCount", sortDir: "desc" }}
        onChange={vi.fn()}
        noun="authors"
      />,
    );

    expect(screen.getByRole("combobox", { name: "Sort authors by" })).toHaveTextContent(
      "Number of books",
    );
    expect(
      screen.getByRole("button", { name: "Descending - switch to ascending" }),
    ).toBeInTheDocument();
  });

  it("flips the direction through the toggle", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <SortControl
        config={AUTHOR_SORT}
        sort={{ sortBy: "bookCount", sortDir: "desc" }}
        onChange={onChange}
        noun="authors"
      />,
    );

    await user.click(screen.getByRole("button", { name: /switch to ascending/ }));

    expect(onChange).toHaveBeenCalledWith({ sortBy: "bookCount", sortDir: "asc" });
  });

  it("picks another key in that key's natural direction", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<SortControl config={AUTHOR_SORT} sort={{}} onChange={onChange} noun="authors" />);

    await user.click(screen.getByRole("combobox", { name: "Sort authors by" }));
    await user.click(await screen.findByRole("option", { name: "Number of books" }));

    expect(onChange).toHaveBeenCalledWith({ sortBy: "bookCount", sortDir: "desc" });
  });
});
