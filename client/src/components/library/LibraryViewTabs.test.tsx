import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";
import { LibraryViewTabs } from "./LibraryViewTabs";

describe("LibraryViewTabs", () => {
  it("renders Books, Series, Authors, and Releases tabs with the active state", async () => {
    render(<RouterTestWrapper ui={<LibraryViewTabs activeTab="series" />} />);

    expect(await screen.findByRole("tab", { name: /books/i })).toHaveAttribute(
      "aria-selected",
      "false",
    );
    expect(screen.getByRole("tab", { name: /series/i })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: /authors/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /releases/i })).toBeInTheDocument();
  });

  // A route tab must be a real link, not a button that calls navigate(): otherwise middle-click,
  // Ctrl/Cmd-click and "Open in new tab" do nothing (DESIGN.md section 3).
  it("renders each tab as a link to its route", async () => {
    render(<RouterTestWrapper ui={<LibraryViewTabs activeTab="books" />} />);

    const tabs = await screen.findAllByRole("tab");
    expect(tabs.map((tab) => [tab.tagName, tab.getAttribute("href")])).toEqual([
      ["A", "/library"],
      ["A", "/library/series"],
      ["A", "/library/authors"],
      ["A", "/library/upcoming-releases"],
    ]);
  });
});
