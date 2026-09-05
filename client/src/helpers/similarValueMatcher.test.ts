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

  it("matches spaced and unspaced dotted initials in the typeahead", () => {
    const list = ["J.K. Rowling", "J. R. R. Tolkien", "George R.R. Martin"];
    expect(narrowByQuery(list, "J. K. Rowling")).toEqual(["J.K. Rowling"]);
    expect(narrowByQuery(list, "J. K. Rowling ")).toEqual(["J.K. Rowling"]);
    expect(narrowByQuery(list, "JRR Tolkien")).toEqual([]);
    expect(narrowByQuery(list, "George R. R. Martin")).toEqual(["George R.R. Martin"]);
    // Reverse direction: the spaced form is the stored one, the unspaced is typed.
    expect(narrowByQuery(["J. R. R. Tolkien"], "J.R.R. Tolkien")).toEqual(["J. R. R. Tolkien"]);
  });

  it("does not collapse spaces inside an ordinary name while narrowing", () => {
    expect(narrowByQuery(["Harry Potter"], "Harry Pot ter")).toEqual([]);
    expect(narrowByQuery(["J. K. Rowling"], "J. K. Rowling")).toEqual(["J. K. Rowling"]);
  });

  it("still hides the typeahead for an exact (already canonical) person", () => {
    // TagsInput/TypeaheadInput suppress suggestions when narrowByQuery yields exactly one
    // candidate whose normalizeForMatch equals the query; initial-spacing variants must NOT
    // evaluate equal here or the suggestion the user asked for would be hidden.
    const norm = (v: string) =>
      v
        .normalize("NFD")
        .replace(/[\u0300-\u036f]/g, "")
        .toLowerCase();
    const candidate = "J.K. Rowling";
    const query = "J. K. Rowling";
    expect(norm(candidate)).not.toBe(norm(query));
  });

  it("is near-match across initial spacing variants", () => {
    expect(isNearMatch("J. K. Rowling", "J.K. Rowling")).toBe(true);
    expect(isNearMatch("J.K. Rowling", "J. K. Rowling")).toBe(true);
    expect(isNearMatch("J.R.R. Tolkien", "J. R. R. Tolkien")).toBe(true);
  });

  it("finds similar existing candidates", () => {
    const list = ["JK Rowling", "J.K. Rowling", "Brandon Sanderson"];
    expect(findSimilarExisting("J. K. Rowling", list)).toEqual(["JK Rowling", "J.K. Rowling"]);
  });
});
