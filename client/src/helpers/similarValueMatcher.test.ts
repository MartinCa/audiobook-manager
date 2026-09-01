import { describe, it, expect } from "vitest";
import {
  foldAccents,
  normalizeForMatch,
  isNearMatch,
  narrowByQuery,
  findSimilarExisting,
} from "./similarValueMatcher";

describe("similarValueMatcher helper", () => {
  it("folds accents", () => {
    expect(foldAccents("René")).toBe("Rene");
  });

  it("normalizes for matching", () => {
    expect(normalizeForMatch(" J.K. Rowling ")).toBe("jk rowling");
  });

  it("checks near matches", () => {
    expect(isNearMatch("Harry Potter", "harry potter")).toBe(true);
    expect(isNearMatch("Rowling", "J.K. Rowling")).toBe(true);
  });

  it("narrows candidates by query", () => {
    const list = ["Brandon Sanderson", "J.K. Rowling", "George R.R. Martin"];
    expect(narrowByQuery(list, "Sanderson")).toEqual(["Brandon Sanderson"]);
  });

  it("finds similar existing candidates", () => {
    const list = ["JK Rowling", "J.K. Rowling", "Brandon Sanderson"];
    expect(findSimilarExisting("J. K. Rowling", list)).toEqual([
      "JK Rowling",
      "J.K. Rowling",
    ]);
  });
});
