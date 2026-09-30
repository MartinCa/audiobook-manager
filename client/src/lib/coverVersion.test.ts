import { afterEach, describe, expect, it, vi } from "vitest";

describe("coverVersion", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.resetModules();
  });

  it("leaves the URL untouched until a cover has been saved", async () => {
    const { withCoverVersion } = await import("./coverVersion");
    expect(withCoverVersion("/api/browse/audiobooks/1/cover")).toBe(
      "/api/browse/audiobooks/1/cover",
    );
  });

  it("appends a new version after a save so the browser cannot reuse the old image", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(1000);
    const { bumpCoverVersion, withCoverVersion } = await import("./coverVersion");
    bumpCoverVersion();
    const first = withCoverVersion("/api/browse/audiobooks/1/cover");
    vi.setSystemTime(2000);
    bumpCoverVersion();
    const second = withCoverVersion("/api/browse/audiobooks/1/cover");

    expect(first).toBe("/api/browse/audiobooks/1/cover?v=1000");
    expect(second).toBe("/api/browse/audiobooks/1/cover?v=2000");
  });

  it("uses & when the URL already has a query string", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(5);
    const { bumpCoverVersion, withCoverVersion } = await import("./coverVersion");
    bumpCoverVersion();
    expect(withCoverVersion("/api/files/cover?path=%2Fa")).toBe("/api/files/cover?path=%2Fa&v=5");
  });
});
