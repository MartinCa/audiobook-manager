import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { LinkButton } from "./LinkButton";

describe("LinkButton", () => {
  it("renders the render target as a link without the Base UI native-button warning", () => {
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});

    render(<LinkButton render={<a href="/library" />}>Back to Library</LinkButton>);

    const baseUiNativeButtonWarning = errorSpy.mock.calls.some((args) =>
      String(args[0]).toLowerCase().includes("native <button>"),
    );
    expect(baseUiNativeButtonWarning).toBe(false);

    errorSpy.mockRestore();

    const link = screen.getByRole("button", { name: "Back to Library" });
    expect(link.tagName).toBe("A");
    expect(link.getAttribute("href")).toBe("/library");
  });

  it("forwards variant, size, className, and children to the rendered element", () => {
    render(
      <LinkButton variant="ghost" size="sm" className="custom-class" render={<a href="/library" />}>
        Back to Library
      </LinkButton>,
    );

    const link = screen.getByRole("button", { name: "Back to Library" });
    expect(link.tagName).toBe("A");
    expect(link).toHaveClass("custom-class");
    expect(link).toHaveClass("h-7", "hover:bg-muted");
    expect(link.textContent).toBe("Back to Library");
  });
});
