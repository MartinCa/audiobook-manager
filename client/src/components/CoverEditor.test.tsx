import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { CoverEditor } from "./CoverEditor";

describe("CoverEditor", () => {
  it("shows the cover image when coverUrl is given", () => {
    render(<CoverEditor coverUrl="/api/files/cover?path=x" onCoverChange={vi.fn()} />);

    const img = screen.getByAltText<HTMLImageElement>("Cover Preview");
    expect(img.src).toContain("/api/files/cover?path=x");
  });

  // Regression: coverUrl is passed unconditionally by callers (there's no cheap way to know
  // ahead of time whether a book has a cover on disk), so it 404s whenever one doesn't. Without
  // falling back, that showed a broken-image icon instead of the "Click to set cover" placeholder
  // every other no-cover case uses.
  it("falls back to the placeholder when coverUrl fails to load", () => {
    render(<CoverEditor coverUrl="/api/files/cover?path=missing" onCoverChange={vi.fn()} />);

    const img = screen.getByAltText("Cover Preview");
    fireEvent.error(img);

    expect(screen.queryByAltText("Cover Preview")).not.toBeInTheDocument();
    expect(screen.getByText("Click to set cover")).toBeInTheDocument();
  });

  it("shows the placeholder when no coverUrl or base64Data is given", () => {
    render(<CoverEditor onCoverChange={vi.fn()} />);

    expect(screen.queryByAltText("Cover Preview")).not.toBeInTheDocument();
    expect(screen.getByText("Click to set cover")).toBeInTheDocument();
  });
});
