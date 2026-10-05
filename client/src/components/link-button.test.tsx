import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { LinkButton } from "./link-button";

describe("LinkButton", () => {
  it("renders the render target as a real link, not a role=button", () => {
    render(<LinkButton render={<a href="/library" />}>Back to Library</LinkButton>);

    const link = screen.getByRole("link", { name: "Back to Library" });
    expect(link.tagName).toBe("A");
    expect(link.getAttribute("href")).toBe("/library");
    expect(link.getAttribute("role")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("applies the button variants and merges className from the caller and the render element", () => {
    render(
      <LinkButton
        variant="ghost"
        size="sm"
        className="caller-class"
        render={<a href="/library" className="element-class" />}
      >
        Back
      </LinkButton>,
    );

    const link = screen.getByRole("link", { name: "Back" });
    expect(link).toHaveClass("caller-class", "element-class", "h-7", "hover:bg-muted");
    expect(link.getAttribute("data-slot")).toBe("button");
  });

  it("keeps content nested inside the render element when it is given no children", () => {
    render(<LinkButton render={<a href="/x">Nested</a>} />);

    expect(screen.getByRole("link", { name: "Nested" })).toBeInTheDocument();
  });

  it("uses its own children over the render element's when it is given some", () => {
    render(<LinkButton render={<a href="/x">Nested</a>}>Own</LinkButton>);

    expect(screen.getByRole("link", { name: "Own" })).toBeInTheDocument();
    expect(screen.queryByText("Nested")).toBeNull();
  });
});
