import { describe, expect, it } from "vitest";
import {
  canonicalizeSeries,
  chooseSeriesPrimary,
  currentSeriesSet,
  formatSeriesSet,
  resolveSeriesSet,
  seriesSetsDiffer,
  withPrimarySeriesFirst,
} from "@/helpers/seriesRelations";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

const set = (series: string, part: string | undefined, ...more: [string, string?][]) =>
  currentSeriesSet(
    series,
    part,
    more.map(([seriesName, seriesPart]) => ({ seriesName, seriesPart })),
  );

describe("canonicalizeSeries", () => {
  it("trims, drops blanks, merges duplicates case-insensitively and orders by name", () => {
    expect(
      canonicalizeSeries([
        { name: " Zed " },
        { name: "", part: "1" },
        { name: "alpha", part: "2" },
        { name: "ALPHA", part: "9" },
        { name: "zed", part: "4" },
      ]),
    ).toEqual([
      { name: "alpha", part: "2" },
      { name: "Zed", part: "4" },
    ]);
  });
});

describe("chooseSeriesPrimary", () => {
  const source = canonicalizeSeries([{ name: "A" }, { name: "B" }, { name: "C" }]);

  it("prefers the explicit choice, then the book's current primary, then any series it already has, then the first", () => {
    const current = [{ name: "X" }, { name: "C" }, { name: "B" }];
    expect(chooseSeriesPrimary(current, source, "A")?.name).toBe("A");
    expect(chooseSeriesPrimary(current, source)?.name).toBe("C");
    expect(chooseSeriesPrimary([], source)?.name).toBe("A");
  });

  it("ignores an explicit choice the source does not have", () => {
    expect(chooseSeriesPrimary([{ name: "B" }], source, "Nope")?.name).toBe("B");
  });

  it("has no primary for an empty source", () => {
    expect(chooseSeriesPrimary([{ name: "B" }], [])).toBeUndefined();
  });
});

describe("seriesSetsDiffer", () => {
  const current = set("Main", "1", ["Spinoff", "3"]);

  it("does not differ for the same series listed in a different source order", () => {
    const fromSource = (entries: { name: string; part?: string }[]) =>
      resolveSeriesSet([{ name: "Main" }, { name: "Spinoff" }], entries);
    expect(
      seriesSetsDiffer(
        current,
        fromSource([
          { name: "Spinoff", part: "3" },
          { name: "Main", part: "1" },
        ]),
      ),
    ).toBe(false);
    expect(
      seriesSetsDiffer(
        current,
        fromSource([
          { name: "Main", part: "1" },
          { name: "Spinoff", part: "3" },
        ]),
      ),
    ).toBe(false);
  });

  it("differs for an added series, a removed series, a changed part and a changed primary", () => {
    const base = [{ name: "Main" }, { name: "Spinoff" }];
    expect(
      seriesSetsDiffer(
        current,
        resolveSeriesSet(base, [
          { name: "Main", part: "1" },
          { name: "Spinoff", part: "3" },
          { name: "Third" },
        ]),
      ),
    ).toBe(true);
    expect(seriesSetsDiffer(current, resolveSeriesSet(base, [{ name: "Main", part: "1" }]))).toBe(
      true,
    );
    expect(
      seriesSetsDiffer(
        current,
        resolveSeriesSet(base, [
          { name: "Main", part: "1" },
          { name: "Spinoff", part: "4" },
        ]),
      ),
    ).toBe(true);
    expect(
      seriesSetsDiffer(
        current,
        resolveSeriesSet(
          base,
          [
            { name: "Main", part: "1" },
            { name: "Spinoff", part: "3" },
          ],
          "Spinoff",
        ),
      ),
    ).toBe(true);
  });

  it("treats a cosmetically different part as the same and a casing change of the name as different", () => {
    expect(
      seriesSetsDiffer(
        set("Main", "1"),
        resolveSeriesSet([{ name: "Main" }], [{ name: "Main", part: "1.0" }]),
      ),
    ).toBe(false);
    expect(
      seriesSetsDiffer(
        set("main", "1"),
        resolveSeriesSet([{ name: "main" }], [{ name: "Main", part: "1" }]),
      ),
    ).toBe(true);
  });

  it("does not differ when neither side has a series", () => {
    expect(seriesSetsDiffer(set("", undefined), resolveSeriesSet([], []))).toBe(false);
  });
});

describe("formatSeriesSet", () => {
  it("marks the primary only when there is more than one series", () => {
    expect(formatSeriesSet(set("Main", "1"))).toBe("Main #1");
    expect(formatSeriesSet(set("Main", "1", ["Spinoff", "3"]))).toBe(
      "Main #1 (primary); Spinoff #3",
    );
    expect(formatSeriesSet(set("", undefined))).toBe("");
  });
});

describe("withPrimarySeriesFirst", () => {
  const result = {
    url: "u",
    cleanUrl: "u",
    source: "Audible",
    authors: [],
    narrators: [],
    bookName: "B",
    genres: [],
    series: [
      { seriesName: "Spinoff", seriesPart: "3", originalSeriesName: "Spinoff Raw" },
      { seriesName: "Main", seriesPart: "1" },
    ],
  } satisfies MetadataSearchResult;

  it("puts the book's current primary first when the source still lists it", () => {
    const out = withPrimarySeriesFirst(result, [{ name: "Main" }]);
    expect(out.series.map((s) => s.seriesName)).toEqual(["Main", "Spinoff"]);
    expect(out.series[1]?.originalSeriesName).toBe("Spinoff Raw");
  });

  it("puts the explicit choice first", () => {
    const out = withPrimarySeriesFirst(result, [{ name: "Main" }], "Spinoff");
    expect(out.series.map((s) => s.seriesName)).toEqual(["Spinoff", "Main"]);
  });
});
