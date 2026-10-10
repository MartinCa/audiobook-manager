import { describe, it, expect } from "vitest";
import { activeFilterValues, applyPresetTo, readPresetFilters, sameFilters } from "./filterPresets";

describe("activeFilterValues", () => {
  it("drops cleared filters and empty lists, keeping false as a real choice", () => {
    expect(
      activeFilterValues({
        followed: false,
        hasMissingBooks: undefined,
        sources: [],
        queueStates: ["NotQueued"],
        minOwnedBooks: 0,
      }),
    ).toEqual({ followed: false, queueStates: ["NotQueued"], minOwnedBooks: 0 });
  });
});

describe("readPresetFilters", () => {
  it("keeps only values a filter can have, so a stored preset cannot smuggle anything else into the route", () => {
    expect(
      readPresetFilters({
        sources: ["Unsupported"],
        followed: true,
        minOwnedBooks: 2,
        refreshedAfter: "2026-06-01T00:00:00.000Z",
        nested: { a: 1 },
        mixed: ["a", 1],
        nothing: null,
        notFinite: Number.POSITIVE_INFINITY,
      }),
    ).toEqual({
      sources: ["Unsupported"],
      followed: true,
      minOwnedBooks: 2,
      refreshedAfter: "2026-06-01T00:00:00.000Z",
    });
  });

  it("drops empty lists", () => {
    expect(readPresetFilters({ genres: [] })).toEqual({});
  });
});

describe("sameFilters", () => {
  it("ignores key order, cleared keys and the order of a list filter", () => {
    expect(
      sameFilters(
        { sources: ["Hardcover", "Audible"], followed: undefined, queueStates: ["NotQueued"] },
        { queueStates: ["NotQueued"], sources: ["Audible", "Hardcover"] },
      ),
    ).toBe(true);
  });

  it("tells apart a different value, an extra filter and a missing one", () => {
    expect(sameFilters({ followed: true }, { followed: false })).toBe(false);
    expect(sameFilters({ followed: true }, { followed: true, hasMissingBooks: true })).toBe(false);
    expect(sameFilters({ followed: true, hasMissingBooks: true }, { followed: true })).toBe(false);
    expect(sameFilters({ sources: ["a"] }, { sources: ["a", "b"] })).toBe(false);
    expect(sameFilters({ followed: false }, {})).toBe(false);
  });

  it("treats two empty filter sets as the same", () => {
    expect(sameFilters({}, { sources: [], followed: undefined })).toBe(true);
  });
});

describe("applyPresetTo", () => {
  it("replaces the filters: every filter currently set is cleared, then the preset's are applied", () => {
    const next = applyPresetTo(
      { followed: true, sources: ["Hardcover"], minOwnedBooks: 3 },
      { sources: ["Unsupported"], queueStates: ["NotQueued"] },
    );

    expect(next).toEqual({
      followed: undefined,
      minOwnedBooks: undefined,
      sources: ["Unsupported"],
      queueStates: ["NotQueued"],
    });
    // The cleared keys are present (as undefined) - that is how the lists' route handlers drop a
    // search param - rather than simply missing.
    expect("followed" in next).toBe(true);
  });

  it("applies onto an unfiltered list", () => {
    expect(applyPresetTo({}, { queueStates: ["NotQueued"] })).toEqual({
      queueStates: ["NotQueued"],
    });
  });
});
