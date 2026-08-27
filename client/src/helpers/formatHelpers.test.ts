import { describe, it, expect } from "vitest";
import { formatDuration } from "./formatHelpers";

describe("formatDuration", () => {
  it("formats hours and minutes when there is at least one full hour", () => {
    expect(formatDuration(3661)).toBe("1h 1m");
  });

  it("formats minutes only when under an hour", () => {
    expect(formatDuration(59 * 60)).toBe("59m");
  });

  it("formats zero seconds as 0m", () => {
    expect(formatDuration(0)).toBe("0m");
  });

  it("omits minutes text but still shows 0m for an exact hour boundary", () => {
    expect(formatDuration(3600)).toBe("1h 0m");
  });

  it("floors partial minutes/hours (truncates rather than rounds)", () => {
    expect(formatDuration(3659)).toBe("1h 0m");
    expect(formatDuration(119)).toBe("1m");
  });

  it("handles multi-hour durations", () => {
    expect(formatDuration(10 * 3600 + 45 * 60)).toBe("10h 45m");
  });
});
