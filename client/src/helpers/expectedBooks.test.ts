import { describe, it, expect, vi, afterEach } from "vitest";
import { isBookUpcoming, utcToday } from "./expectedBooks";

afterEach(() => {
  vi.useRealTimers();
});

describe("utcToday", () => {
  // Regression for the review finding: the classifier's default "today" must be the UTC calendar
  // date the backend's DateOnly.FromDateTime(DateTime.UtcNow) compares against, never the
  // browser's local date - a release dated exactly at the UTC-vs-local day boundary was
  // classified by the backend into one section and re-classified away by the client into none.
  // Anchoring at UTC midnight makes the derived date parts timezone-independent by construction.
  it("anchors today at UTC midnight of the UTC calendar date", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-09-21T01:30:00Z"));

    // The local representation of that instant may be a different calendar day; the UTC one must
    // win, and the anchor must be UTC midnight so the getUTC* parts match DateOnly exactly.
    expect(utcToday().toISOString()).toBe("2026-09-21T00:00:00.000Z");
  });
});

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

  it("does not classify a release dated exactly today as upcoming", () => {
    // Upcoming means strictly after today - the same rule as
    // ExpectedBookClassifier.IsUpcoming's releaseDate > today comparison.
    expect(isBookUpcoming("2026-09-20", 2025, today)).toBe(false);
    expect(isBookUpcoming("2026-09-21", 2030, today)).toBe(true);
  });

  // Regression for the review finding: the default today derives from the UTC calendar date (see
  // utcToday), so the boundary behaves the same wherever the browser runs. At this instant the
  // UTC date is the 20th in every timezone - a release dated the 21st is upcoming even if the
  // local date has already advanced past it.
  it("uses the UTC calendar date for the default today, not the local one", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-09-20T23:30:00Z"));

    expect(isBookUpcoming("2026-09-21", 2025)).toBe(true);
    expect(isBookUpcoming("2026-09-20", 2030)).toBe(false);
  });
});
