import { describe, it, expect } from "vitest";
import {
  AUTHOR_SORT,
  BOOK_SORT,
  SERIES_SORT,
  effectiveSort,
  sortAfterDirectionToggle,
  sortAfterKeyChange,
  sortSearchFields,
  toListSort,
} from "./listSort";
import { z } from "zod";

describe("listSort", () => {
  it("every config's default key is one of its options, and keys are unique", () => {
    for (const config of [AUTHOR_SORT, SERIES_SORT, BOOK_SORT]) {
      const keys = config.options.map((o) => o.value);
      expect(keys).toContain(config.defaultKey);
      expect(new Set(keys).size).toBe(keys.length);
    }
  });

  it("offers the requested keys on each list", () => {
    expect(AUTHOR_SORT.options.map((o) => o.value)).toEqual([
      "name",
      "bookCount",
      "lastRefreshed",
      "missingBooks",
      "upcomingBooks",
    ]);
    expect(SERIES_SORT.options.map((o) => o.value)).toEqual([
      "name",
      "bookCount",
      "lastRefreshed",
      "missingBooks",
      "completion",
    ]);
    expect(BOOK_SORT.options.map((o) => o.value)).toEqual([
      "title",
      "year",
      "duration",
      "lastRefreshed",
      "series",
    ]);
  });

  it("falls back to the default key ascending when the URL has no sort", () => {
    expect(effectiveSort(AUTHOR_SORT, {})).toEqual({ sortBy: "name", sortDir: "asc" });
    expect(effectiveSort(BOOK_SORT, { sortBy: "year" })).toEqual({
      sortBy: "year",
      sortDir: "asc",
    });
  });

  it("clears the URL sort for the default key ascending, keeping both fields present", () => {
    const cleared = toListSort(AUTHOR_SORT, "name", "asc");
    expect(cleared).toEqual({ sortBy: undefined, sortDir: undefined });
    expect(Object.keys(cleared)).toEqual(["sortBy", "sortDir"]);
    expect(toListSort(AUTHOR_SORT, "name", "desc")).toEqual({ sortBy: "name", sortDir: "desc" });
  });

  it("starts a newly picked key in its natural direction", () => {
    expect(sortAfterKeyChange(AUTHOR_SORT, "bookCount")).toEqual({
      sortBy: "bookCount",
      sortDir: "desc",
    });
    expect(sortAfterKeyChange(BOOK_SORT, "series")).toEqual({
      sortBy: "series",
      sortDir: "asc",
    });
    // Back to the default key: the URL goes clean again.
    expect(sortAfterKeyChange(AUTHOR_SORT, "name")).toEqual({
      sortBy: undefined,
      sortDir: undefined,
    });
  });

  it("flips the direction of the effective sort, including the default one", () => {
    expect(sortAfterDirectionToggle(AUTHOR_SORT, {})).toEqual({
      sortBy: "name",
      sortDir: "desc",
    });
    expect(sortAfterDirectionToggle(AUTHOR_SORT, { sortBy: "bookCount", sortDir: "desc" })).toEqual(
      { sortBy: "bookCount", sortDir: "asc" },
    );
    expect(sortAfterDirectionToggle(AUTHOR_SORT, { sortBy: "name", sortDir: "desc" })).toEqual({
      sortBy: undefined,
      sortDir: undefined,
    });
  });

  it("drops an unknown key or direction from a route search instead of failing it", () => {
    const schema = z.object(sortSearchFields(AUTHOR_SORT));
    expect(schema.parse({ sortBy: "bookCount", sortDir: "desc" })).toEqual({
      sortBy: "bookCount",
      sortDir: "desc",
    });
    expect(schema.parse({ sortBy: "year", sortDir: "sideways" })).toEqual({
      sortBy: undefined,
      sortDir: undefined,
    });
    expect(schema.parse({})).toEqual({});
  });
});
