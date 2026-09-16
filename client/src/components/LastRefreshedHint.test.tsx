import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { LastRefreshedHint } from "./LastRefreshedHint";

describe("LastRefreshedHint", () => {
  it("renders the shared 'never' state", () => {
    render(<LastRefreshedHint lastRefreshedAt={null} />);
    expect(screen.getByText(/Last refreshed from source:/)).toBeInTheDocument();
    expect(screen.getByText(/Never/)).toBeInTheDocument();
  });

  it("formats a timestamp the way the book detail does", () => {
    // Matches the book detail's own render from the same helper, so a book and a series answer
    // the same question the same way (that is the whole point of the shared component).
    render(<LastRefreshedHint lastRefreshedAt="2026-09-01T12:30:00Z" />);
    expect(screen.getByText(/2026-09-01/)).toBeInTheDocument();
  });
});
