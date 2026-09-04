import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { CoverEditor } from "./CoverEditor";

// The real helper asks the browser to decode the image so it can shrink it, and jsdom's <img>
// never settles for a blob URL - so it would sit out its decode timeout in every test here.
// coverImage.test.ts covers the helper itself; these tests are about the component.
vi.mock("@/lib/coverImage", () => ({
  prepareCover: vi.fn((blob: Blob) =>
    Promise.resolve({ base64Data: "cHJlcGFyZWQ=", mimeType: blob.type || "image/jpeg" }),
  ),
}));

describe("CoverEditor", () => {
  it("shows the cover image when coverUrl is given", () => {
    render(<CoverEditor coverUrl="/api/files/cover?path=x" onCoverChange={vi.fn()} />);

    const img = screen.getByAltText<HTMLImageElement>("Cover Preview");
    expect(img.src).toContain("/api/files/cover?path=x");
  });

  it("fits the whole cover inside the square box without cropping", () => {
    render(<CoverEditor coverUrl="/api/files/cover?path=x" onCoverChange={vi.fn()} />);

    expect(screen.getByAltText<HTMLImageElement>("Cover Preview")).toHaveClass("object-contain");
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

  it("fetches image from URL and calls onCoverChange with base64 data", async () => {
    const onCoverChange = vi.fn();
    const mockBlob = new Blob(["fake-image-bytes"], { type: "image/png" });

    class MockFileReader {
      result = "data:image/png;base64,ZmFrZS1pbWFnZQ==";
      onloadend: (() => void) | null = null;
      onerror: (() => void) | null = null;
      readAsDataURL() {
        queueMicrotask(() => {
          this.onloadend?.();
        });
      }
    }
    vi.stubGlobal("FileReader", MockFileReader);

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      blob: vi.fn().mockResolvedValue(mockBlob),
    });

    render(<CoverEditor onCoverChange={onCoverChange} />);

    // Open cover dialog
    const openBtn = screen.getByRole("button", { name: /upload cover/i });
    fireEvent.click(openBtn);

    const input = screen.getByPlaceholderText("https://example.com/cover.jpg");
    fireEvent.change(input, { target: { value: "https://example.com/cover.png" } });

    const fetchBtn = screen.getByRole("button", { name: "Fetch" });
    fireEvent.click(fetchBtn);

    await vi.waitFor(() => {
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/metadata-search/proxy-image?url=https%3A%2F%2Fexample.com%2Fcover.png",
      );
      expect(onCoverChange).toHaveBeenCalled();
    });
  });
});
