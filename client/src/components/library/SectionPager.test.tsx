import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SectionPager } from "./SectionPager";

describe("SectionPager", () => {
  it("clamps the previous/next buttons and reports the requested page", () => {
    const onPageChange = vi.fn();
    const { rerender } = render(
      <SectionPager currentPage={0} pageCount={3} totalCount={120} onPageChange={onPageChange} />,
    );

    expect(screen.getByText("Showing 1–50 of 120")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Previous" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(onPageChange).toHaveBeenCalledWith(1);

    rerender(
      <SectionPager currentPage={2} pageCount={3} totalCount={120} onPageChange={onPageChange} />,
    );
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Previous" }));
    expect(onPageChange).toHaveBeenCalledWith(1);
  });

  it("computes the shown range from a custom pageSize instead of the default", () => {
    const onPageChange = vi.fn();
    render(
      <SectionPager
        currentPage={1}
        pageCount={4}
        totalCount={65}
        pageSize={20}
        onPageChange={onPageChange}
      />,
    );

    expect(screen.getByText("Showing 21–40 of 65")).toBeInTheDocument();
  });

  it("omits the rows-per-page dropdown when onPageSizeChange is not passed", () => {
    render(<SectionPager currentPage={0} pageCount={3} totalCount={120} onPageChange={vi.fn()} />);

    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
  });

  it("renders the rows-per-page dropdown and reports the selected size", async () => {
    const onPageSizeChange = vi.fn();
    const user = userEvent.setup();
    render(
      <SectionPager
        currentPage={0}
        pageCount={3}
        totalCount={120}
        pageSize={50}
        onPageChange={vi.fn()}
        onPageSizeChange={onPageSizeChange}
      />,
    );

    const combo = screen.getByRole("combobox", { name: "Rows per page" });
    expect(combo).toHaveTextContent("50 / page");
    await user.click(combo);
    // The option list renders into a portal after the popup opens, so it isn't there
    // synchronously on click - wait for it instead of a bare getByRole.
    await user.click(await screen.findByRole("option", { name: "100 / page" }));

    expect(onPageSizeChange).toHaveBeenCalledWith(100);
  });
});
