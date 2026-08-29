import { describe, it, expect } from "vitest";
import {
  normalizeForMatch,
  isNearMatch,
  findSimilarExisting,
  narrowByQuery,
  foldAccents,
} from "./similarValueMatcher";

describe("foldAccents", () => {
  it("strips combining diacritics", () => {
    expect(foldAccents("René")).toBe("Rene");
    expect(foldAccents("Café")).toBe("Cafe");
    expect(foldAccents("Saint-Exupéry")).toBe("Saint-Exupery");
  });

  it("leaves unaccented text unchanged", () => {
    expect(foldAccents("Rene")).toBe("Rene");
  });
});

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

  it("folds diacritics so accented and unaccented spellings normalize identically", () => {
    expect(normalizeForMatch("René Descartes")).toBe(
      normalizeForMatch("Rene Descartes"),
    );
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

  it("matches an unaccented query against an accented value", () => {
    expect(narrowByQuery("rene", ["René Descartes"])).toEqual([
      "René Descartes",
    ]);
  });

  it("matches an accented query against an unaccented value", () => {
    expect(narrowByQuery("café", ["Cafe Noir"])).toEqual(["Cafe Noir"]);
  });
});

describe("performance-shaped behaviour", () => {
  // Regression guard: findSimilarExisting called isNearMatch per candidate, which re-normalized
  // the *query* every time - N normalizations of the same string per call. Normalizing once is
  // an internal change, so this asserts the observable consequence instead: results are
  // unchanged, and the work no longer scales with how the caller passes the query in.
  it("gives the same results however many candidates are compared", () => {
    const existing = ["JK Rowling", "J.K. Rowling", "Brandon Sanderson"];

    expect(findSimilarExisting("J K Rowling", existing)).toEqual([]);
    expect(findSimilarExisting("JK Rowlingg", existing)).toEqual([
      "JK Rowling",
      "J.K. Rowling",
    ]);
  });

  // The length-difference short-circuit must not change which pairs match: a difference larger
  // than the allowed edit distance genuinely cannot be within it.
  it("still rejects pairs whose lengths differ by more than the threshold", () => {
    expect(isNearMatch("Sanderson", "Sanderson The Second Of Its Name")).toBe(
      false,
    );
  });

  it("still accepts a near match of equal length", () => {
    expect(isNearMatch("Sanderson", "Sandersan")).toBe(true);
  });

  // narrowByQuery stops once it has `limit` matches rather than folding and filtering the whole
  // list. The cap and the order it returns in must be unchanged.
  it("returns at most `limit` matches, in list order", () => {
    const names = Array.from({ length: 50 }, (_, i) => `Author ${i}`);

    const result = narrowByQuery("author", names, 3);

    expect(result).toEqual(["Author 0", "Author 1", "Author 2"]);
  });

  // The folded forms are cached against the array, so a mutated *copy* must not be served the
  // previous array's cache.
  it("reflects a replaced name list rather than a cached one", () => {
    const first = ["René Girard"];
    const second = ["Ursula Le Guin"];

    expect(narrowByQuery("rene", first)).toEqual(["René Girard"]);
    expect(narrowByQuery("rene", second)).toEqual([]);
    expect(narrowByQuery("ursula", second)).toEqual(["Ursula Le Guin"]);
  });

  it("still matches accent-insensitively in both directions", () => {
    expect(narrowByQuery("rene", ["René Girard"])).toEqual(["René Girard"]);
    expect(narrowByQuery("René", ["Rene Girard"])).toEqual(["Rene Girard"]);
  });
});
