import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { DiffDisplay } from "./DiffDisplay";

describe("DiffDisplay", () => {
  it("renders identical text without addition/removal classes", () => {
    const { container } = render(<DiffDisplay original="Brandon" modified="Brandon" />);
    expect(screen.getByText("Brandon")).toBeInTheDocument();
    expect(container.querySelector(".text-emerald-600")).toBeNull();
    expect(container.querySelector(".text-rose-600")).toBeNull();
  });

  it("highlights added and removed characters", () => {
    const { container } = render(<DiffDisplay actual="Bran" expected="Brandon" />);
    expect(container.querySelector(".text-emerald-600")).toBeInTheDocument();
    expect(screen.getByText("don")).toBeInTheDocument();
  });
});
