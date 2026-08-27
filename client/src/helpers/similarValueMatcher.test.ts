import { describe, it, expect } from "vitest";
import {
  normalizeForMatch,
  isNearMatch,
  findSimilarExisting,
  narrowByQuery,
} from "./similarValueMatcher";

describe("normalizeForMatch", () => {
  it("returns empty string for null/undefined/empty input", () => {
    expect(normalizeForMatch(null)).toBe("");
    expect(normalizeForMatch(undefined)).toBe("");
    expect(normalizeForMatch("")).toBe("");
  });

  it("lowercases and trims", () => {
    expect(normalizeForMatch("  Brandon Sanderson  ")).toBe(
      "brandon sanderson",
    );
  });

  it("replaces & with 'and'", () => {
    expect(normalizeForMatch("Fantasy & Adventure")).toBe(
      "fantasy and adventure",
    );
  });

  it("strips periods and collapses whitespace", () => {
    expect(normalizeForMatch("Robert  Jordan.")).toBe("robert jordan");
  });

  it("merges consecutive single-letter initials into one token", () => {
    expect(normalizeForMatch("J K Rowling")).toBe("jk rowling");
    expect(normalizeForMatch("J.K. Rowling")).toBe("jk rowling");
  });

  it("keeps trailing initials merged when they end the string", () => {
    expect(normalizeForMatch("Rowling J K")).toBe("rowling jk");
  });
});

describe("isNearMatch", () => {
  it("returns false for an exact match (not a 'similar' hint)", () => {
    expect(isNearMatch("Brandon Sanderson", "Brandon Sanderson")).toBe(false);
  });

  it("returns false for values that normalize identically but differ in raw form", () => {
    // Normalizes to the same value ("jk rowling" both ways) -> treated as exact, not "near"
    expect(isNearMatch("J.K. Rowling", "JK Rowling")).toBe(false);
  });

  it("returns true for a small typo within threshold", () => {
    expect(isNearMatch("Brandon Sanderson", "Brandon Sandersn")).toBe(true);
  });

  it("returns false when the difference exceeds the length-scaled threshold", () => {
    expect(isNearMatch("Brandon Sanderson", "Totally Different Name")).toBe(
      false,
    );
  });

  it("returns false for very short strings (threshold 0 for length <= 4)", () => {
    expect(isNearMatch("Amy", "Any")).toBe(false);
  });

  it("returns false when either value is empty/blank", () => {
    expect(isNearMatch("", "Brandon Sanderson")).toBe(false);
    expect(isNearMatch("Brandon Sanderson", "")).toBe(false);
    expect(isNearMatch("   ", "Brandon Sanderson")).toBe(false);
  });
});

describe("findSimilarExisting", () => {
  it("returns matching near-duplicates and excludes exact matches", () => {
    const existing = [
      "Brandon Sanderson",
      "Brandon Sandersn",
      "Totally Different Author",
    ];
    expect(findSimilarExisting("Brandon Sanderson", existing)).toEqual([
      "Brandon Sandersn",
    ]);
  });

  it("returns an empty array for an empty candidate list", () => {
    expect(findSimilarExisting("Brandon Sanderson", [])).toEqual([]);
  });

  it("returns an empty array when no candidates are similar", () => {
    expect(
      findSimilarExisting("Brandon Sanderson", ["Unrelated Name"]),
    ).toEqual([]);
  });

  it("returns an empty array when value is empty", () => {
    expect(findSimilarExisting("", ["Brandon Sanderson"])).toEqual([]);
  });
});

describe("narrowByQuery", () => {
  const values = ["Brandon Sanderson", "Patrick Rothfuss", "Robin Hobb"];

  it("returns values containing the query, case-insensitively", () => {
    expect(narrowByQuery("robin", values)).toEqual(["Robin Hobb"]);
    expect(narrowByQuery("SAN", values)).toEqual(["Brandon Sanderson"]);
  });

  it("returns an empty array for a blank/empty query", () => {
    expect(narrowByQuery("", values)).toEqual([]);
    expect(narrowByQuery("   ", values)).toEqual([]);
  });

  it("returns an empty array for an empty candidate list", () => {
    expect(narrowByQuery("robin", [])).toEqual([]);
  });

  it("returns an empty array when no candidates match", () => {
    expect(narrowByQuery("zzz", values)).toEqual([]);
  });

  it("respects the limit parameter", () => {
    const many = ["Anna", "Anne", "Annie", "Annette"];
    expect(narrowByQuery("ann", many, 2)).toEqual(["Anna", "Anne"]);
  });
});
