import { describe, it, expect } from "vitest";
import { isBookUpcoming } from "./expectedBooks";

describe("isBookUpcoming", () => {
  const today = new Date("2026-09-20T12:00:00Z");

  it("classifies by precise release date first when one is present", () => {
    expect(isBookUpcoming("2026-11-01", 2025, today)).toBe(true);
    expect(isBookUpcoming("2026-01-01", 2030, today)).toBe(false);
  });

  it("falls back to the bare-year heuristic without a precise date", () => {
    expect(isBookUpcoming(null, 2027, today)).toBe(true);
    expect(isBookUpcoming(null, 2026, today)).toBe(false);
    expect(isBookUpcoming(null, null, today)).toBe(false);
  });
});
