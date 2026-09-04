import { describe, it, expect, vi, afterEach } from "vitest";
import {
  COVER_MAX_BYTES,
  COVER_MAX_DIMENSION,
  fitWithinCap,
  prepareCover,
  shouldShrink,
} from "./coverImage";

describe("shouldShrink", () => {
  it("leaves an image that is small in both pixels and bytes alone", () => {
    expect(shouldShrink(800, 800, 400_000)).toBe(false);
  });

  it("shrinks an image past the dimension cap even when its file is small", () => {
    // A flat-artwork cover can be 4000px and only a few hundred KB. Pixels alone have to be
    // enough, or it would be sent at full size.
    expect(shouldShrink(COVER_MAX_DIMENSION + 1, 800, 100_000)).toBe(true);
    expect(shouldShrink(800, COVER_MAX_DIMENSION + 1, 100_000)).toBe(true);
  });

  it("shrinks an image past the byte cap even when its dimensions are fine", () => {
    expect(shouldShrink(1000, 1000, COVER_MAX_BYTES + 1)).toBe(true);
  });

  it("treats exactly at the cap as acceptable", () => {
    expect(shouldShrink(COVER_MAX_DIMENSION, COVER_MAX_DIMENSION, COVER_MAX_BYTES)).toBe(false);
  });
});

describe("fitWithinCap", () => {
  it("preserves the aspect ratio of a non-square cover", () => {
    expect(fitWithinCap(3000, 1500)).toEqual({ width: 1500, height: 750 });
  });

  it("never enlarges an image that is already inside the cap", () => {
    expect(fitWithinCap(400, 300)).toEqual({ width: 400, height: 300 });
  });

  it("keeps a degenerate edge at at least one pixel", () => {
    expect(fitWithinCap(30000, 1).height).toBe(1);
  });
});

describe("prepareCover", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // jsdom's <img> neither loads nor errors for a blob URL, which is exactly the case the decode
  // timeout exists for - and the property that matters when it fires: the cover is still sent.
  // The server normalizes every cover regardless, so a browser that cannot decode one is not a
  // reason to refuse it.
  it("falls back to the original bytes when the image never decodes", async () => {
    const blob = new Blob([new Uint8Array([1, 2, 3, 4])], { type: "image/png" });

    const result = await prepareCover(blob, { decodeTimeoutMs: 10 });

    expect(result.mimeType).toBe("image/png");
    expect(result.base64Data.length).toBeGreaterThan(0);
  });

  it("defaults the mime type when the blob does not carry one", async () => {
    const result = await prepareCover(new Blob([new Uint8Array([1, 2, 3])]), {
      decodeTimeoutMs: 10,
    });

    expect(result.mimeType).toBe("image/jpeg");
  });

  it("returns the original when the decoded image is already within the caps", async () => {
    // Stub the decode so the size check runs without needing a real decoder.
    vi.stubGlobal(
      "Image",
      class {
        naturalWidth = 500;
        naturalHeight = 500;
        onload: (() => void) | null = null;
        onerror: (() => void) | null = null;
        set src(_value: string) {
          setTimeout(() => this.onload?.(), 0);
        }
      },
    );
    vi.stubGlobal("URL", { createObjectURL: () => "blob:stub", revokeObjectURL: () => undefined });

    const blob = new Blob([new Uint8Array([9, 9, 9])], { type: "image/png" });
    const result = await prepareCover(blob);

    expect(result.mimeType).toBe("image/png");
  });
});
