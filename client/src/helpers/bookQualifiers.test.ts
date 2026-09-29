import { describe, it, expect } from "vitest";
import {
  applyQualifiers,
  cleanSearchResult,
  normalizeQualifiers,
  qualifierLabel,
  splitQualifiers,
} from "./bookQualifiers";
import type { BookQualifierOption } from "@/types/BookQualifier";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

// Mirrors what GET /api/settings/book-qualifiers serves; the helpers hold no list of their own.
const options: BookQualifierOption[] = [
  { key: "abridged", label: "Abridged", suffix: " (Abridged)" },
  { key: "dramatized", label: "Dramatized", suffix: " (Dramatized)" },
];

describe("normalizeQualifiers", () => {
  it("sorts alphabetically by label, lowercases and de-duplicates", () => {
    expect(normalizeQualifiers(["Dramatized", "abridged", " DRAMATIZED ", ""], options)).toEqual([
      "abridged",
      "dramatized",
    ]);
  });

  it("keeps an unknown key after the known ones", () => {
    expect(normalizeQualifiers(["zzz-retired", "dramatized"], options)).toEqual([
      "dramatized",
      "zzz-retired",
    ]);
  });

  it("treats null and undefined as no qualifiers", () => {
    expect(normalizeQualifiers(null, options)).toEqual([]);
    expect(normalizeQualifiers(undefined, options)).toEqual([]);
  });

  it("orders by label, not by key, so a future qualifier sorts where the server sorts it", () => {
    const withOddKey: BookQualifierOption[] = [
      { key: "z-first", label: "Alpha", suffix: " (Alpha)" },
      { key: "a-last", label: "Zulu", suffix: " (Zulu)" },
    ];
    expect(normalizeQualifiers(["a-last", "z-first"], withOddKey)).toEqual(["z-first", "a-last"]);
  });
});

describe("qualifierLabel", () => {
  it("returns the display label", () => {
    expect(qualifierLabel("dramatized", options)).toBe("Dramatized");
  });

  it("falls back to the key for one the list does not have", () => {
    expect(qualifierLabel("zzz-retired", options)).toBe("zzz-retired");
  });
});

describe("applyQualifiers", () => {
  it("appends one parenthetical per qualifier, alphabetically", () => {
    expect(applyQualifiers("Killing Floor", ["dramatized", "abridged"], options)).toBe(
      "Killing Floor (Abridged) (Dramatized)",
    );
  });

  it("leaves the name alone with no qualifiers", () => {
    expect(applyQualifiers("Killing Floor", [], options)).toBe("Killing Floor");
  });

  it("ignores an unknown key, as the server does", () => {
    expect(applyQualifiers("Killing Floor", ["zzz-retired"], options)).toBe("Killing Floor");
  });

  it("returns a blank name unchanged", () => {
    expect(applyQualifiers("", ["dramatized"], options)).toBe("");
    expect(applyQualifiers(undefined, ["dramatized"], options)).toBe("");
  });
});

describe("splitQualifiers", () => {
  it("moves a known suffix off the name and the same suffix off the series", () => {
    expect(
      splitQualifiers("Killing Floor (Dramatized)", "Jack Reacher (Dramatized)", options),
    ).toEqual({ bookName: "Killing Floor", series: "Jack Reacher", qualifiers: ["dramatized"] });
  });

  it("finds several suffixes whatever order they are in and reports them canonically", () => {
    expect(splitQualifiers("Killing Floor (Dramatized) (Abridged)", undefined, options)).toEqual({
      bookName: "Killing Floor",
      series: undefined,
      qualifiers: ["abridged", "dramatized"],
    });
  });

  it("leaves a series that does not carry the same suffixes as it was", () => {
    expect(splitQualifiers("Killing Floor (Dramatized)", "Jack Reacher", options)).toEqual({
      bookName: "Killing Floor",
      series: "Jack Reacher",
      qualifiers: ["dramatized"],
    });
    expect(
      splitQualifiers("Killing Floor (Dramatized)", "Jack Reacher (Abridged)", options).series,
    ).toBe("Jack Reacher (Abridged)");
  });

  it("finds nothing in a name without a known suffix", () => {
    expect(splitQualifiers("Killing Floor (Unabridged)", "Jack Reacher", options)).toEqual({
      bookName: "Killing Floor (Unabridged)",
      series: "Jack Reacher",
      qualifiers: [],
    });
  });

  it("is case-insensitive on the label", () => {
    expect(splitQualifiers("Killing Floor (dramatized)", null, options).qualifiers).toEqual([
      "dramatized",
    ]);
  });

  it("never strips the whole name away", () => {
    expect(splitQualifiers(" (Dramatized)", null, options)).toEqual({
      bookName: " (Dramatized)",
      series: undefined,
      qualifiers: [],
    });
  });

  it("only matches a suffix at the end of the name", () => {
    expect(splitQualifiers("The (Dramatized) Killing Floor", null, options).qualifiers).toEqual([]);
  });

  it("is the inverse of applyQualifiers", () => {
    const written = applyQualifiers("Killing Floor", ["abridged", "dramatized"], options);
    expect(splitQualifiers(written, null, options)).toMatchObject({
      bookName: "Killing Floor",
      qualifiers: ["abridged", "dramatized"],
    });
  });
});

describe("cleanSearchResult", () => {
  const result: MetadataSearchResult = {
    url: "https://audible.com/pd/x",
    cleanUrl: "https://audible.com/pd/x",
    source: "Audible",
    authors: [{ name: "Lee Child" }],
    narrators: [],
    bookName: "Killing Floor (Dramatized)",
    series: [
      { seriesName: "Jack Reacher (Dramatized)", seriesPart: "1" },
      { seriesName: "Other (Dramatized)", seriesPart: "2" },
    ],
    genres: [],
  };

  it("cleans the title and the first series and reports the qualifiers found", () => {
    const cleaned = cleanSearchResult(result, options);

    expect(cleaned.qualifiers).toEqual(["dramatized"]);
    expect(cleaned.result.bookName).toBe("Killing Floor");
    // Every series entry carrying the title's suffixes is cleaned, not just the first.
    expect(cleaned.result.series.map((s) => s.seriesName)).toEqual(["Jack Reacher", "Other"]);
  });

  it("leaves a series entry whose suffix set differs from the title's as it was", () => {
    const cleaned = cleanSearchResult(
      {
        ...result,
        series: [
          { seriesName: "Jack Reacher (Dramatized)", seriesPart: "1" },
          { seriesName: "Other (Abridged)", seriesPart: "2" },
          { seriesName: "Plain", seriesPart: "3" },
        ],
      },
      options,
    );

    expect(cleaned.result.series.map((s) => s.seriesName)).toEqual([
      "Jack Reacher",
      "Other (Abridged)",
      "Plain",
    ]);
  });

  it("does not mutate the result it was given", () => {
    cleanSearchResult(result, options);

    expect(result.bookName).toBe("Killing Floor (Dramatized)");
  });

  it("returns the very same object when there is nothing to move", () => {
    const plain = { ...result, bookName: "Killing Floor", series: [] };

    const cleaned = cleanSearchResult(plain, options);

    expect(cleaned.result).toBe(plain);
    expect(cleaned.qualifiers).toEqual([]);
  });
});
