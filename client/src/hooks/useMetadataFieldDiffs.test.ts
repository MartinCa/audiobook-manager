import { describe, it, expect } from "vitest";
import { renderHook } from "@testing-library/react";
import { useMetadataFieldDiffs } from "./useMetadataFieldDiffs";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

function baseSearchResult(overrides: Partial<MetadataSearchResult> = {}): MetadataSearchResult {
  return {
    url: "https://example.com/book",
    cleanUrl: "https://example.com/book",
    source: "Audible",
    authors: [],
    narrators: [],
    bookName: "A Book",
    genres: [],
    series: [],
    ...overrides,
  };
}

function findField(fields: ReturnType<typeof useMetadataFieldDiffs>, key: string) {
  const field = fields.find((f) => f.key === key);
  if (!field) throw new Error(`No field diff for key "${key}"`);
  return field;
}

describe("useMetadataFieldDiffs series", () => {
  const source = (...entries: [string, string?][]) =>
    baseSearchResult({
      series: entries.map(([seriesName, seriesPart]) => ({ seriesName, seriesPart })),
    });

  it("does not flag a change when the source lists the same series in a different order", () => {
    const current: OrganizeAudiobookInput = {
      series: "Main",
      seriesPart: "1",
      additionalSeries: [{ seriesName: "Spinoff", seriesPart: "3" }],
    };

    const { result } = renderHook(() =>
      useMetadataFieldDiffs(current, source(["Spinoff", "3"], ["Main", "1"]), []),
    );

    expect(findField(result.current, "series").changed).toBe(false);
  });

  it("flags an added series and shows both sets with the primary marked", () => {
    const current: OrganizeAudiobookInput = { series: "Main", seriesPart: "1" };

    const { result } = renderHook(() =>
      useMetadataFieldDiffs(current, source(["Main", "1"], ["Spinoff", "3"]), []),
    );

    const series = findField(result.current, "series");
    expect(series.changed).toBe(true);
    expect(series.currentValue).toBe("Main #1");
    expect(series.newValue).toBe("Main #1 (primary); Spinoff #3");
  });

  it("flags a removed series, a changed part and a different chosen primary", () => {
    const current: OrganizeAudiobookInput = {
      series: "Main",
      seriesPart: "1",
      additionalSeries: [{ seriesName: "Spinoff", seriesPart: "3" }],
    };

    expect(
      findField(
        renderHook(() => useMetadataFieldDiffs(current, source(["Main", "1"]), [])).result.current,
        "series",
      ).changed,
    ).toBe(true);
    expect(
      findField(
        renderHook(() =>
          useMetadataFieldDiffs(current, source(["Main", "1"], ["Spinoff", "4"]), []),
        ).result.current,
        "series",
      ).changed,
    ).toBe(true);
    expect(
      findField(
        renderHook(() =>
          useMetadataFieldDiffs(
            current,
            source(["Main", "1"], ["Spinoff", "3"]),
            [],
            false,
            "Spinoff",
          ),
        ).result.current,
        "series",
      ).changed,
    ).toBe(true);
  });

  it("flags a source with no series against a book that has one", () => {
    const current: OrganizeAudiobookInput = { series: "Main", seriesPart: "1" };

    const { result } = renderHook(() => useMetadataFieldDiffs(current, source(), []));

    expect(findField(result.current, "series").changed).toBe(true);
  });
});

describe("useMetadataFieldDiffs", () => {
  // Regression: "Stephen M. R. Covey" (spaced) and "Stephen M.R. Covey" (unspaced) name the
  // same author under a different initials-spacing convention, not a content change - a
  // spurious Authors change used to be flagged on every book scraped from a source that spaces
  // initials differently than the library.
  it("does not flag an Authors change when only initials spacing differs", () => {
    const current: OrganizeAudiobookInput = { authors: "Stephen M. R. Covey" };
    const searchResult = baseSearchResult({
      authors: [{ name: "Stephen M.R. Covey" }],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "authors").changed).toBe(false);
  });

  it("still flags a genuinely different Authors value despite the initials fold", () => {
    const current: OrganizeAudiobookInput = { authors: "Stephen R. Covey" };
    const searchResult = baseSearchResult({
      authors: [{ name: "Stephen M.R. Covey" }],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "authors").changed).toBe(true);
  });

  // Regression (reported live against a real library): "Andrew R. Chow" vs "Andrew R Chow" is a
  // missing-dot difference on a LONE middle initial, not a spacing one - foldInitialSpacing only
  // collapses the space after a dotted initial and does nothing when the dot itself is absent.
  it("does not flag an Authors change when a lone middle initial's dot is missing", () => {
    const current: OrganizeAudiobookInput = { authors: "Andrew R. Chow" };
    const searchResult = baseSearchResult({
      authors: [{ name: "Andrew R Chow" }],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "authors").changed).toBe(false);
  });

  // Regression: the backend's InitialsSpacingFormatter.Format splits on ' ' and drops empty
  // entries, so a doubled space inside a name is collapsed before it compares - the pending-row
  // chips did not list Authors, but this hook compared the raw strings and showed an Authors row
  // whose current and new values looked identical (HTML collapses the extra space).
  it("does not flag an Authors change when only internal whitespace differs", () => {
    const current: OrganizeAudiobookInput = {
      authors: "David Weber, Timothy Zahn, Thomas  Pope",
    };
    const searchResult = baseSearchResult({
      authors: [{ name: "David Weber" }, { name: "Timothy Zahn" }, { name: "Thomas Pope" }],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "authors").changed).toBe(false);
  });

  it("still flags a genuinely different lone middle initial", () => {
    const current: OrganizeAudiobookInput = { authors: "Andrew R. Chow" };
    const searchResult = baseSearchResult({
      authors: [{ name: "Andrew S Chow" }],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "authors").changed).toBe(true);
  });

  it("does not flag a Narrators change when only initials spacing differs", () => {
    const current: OrganizeAudiobookInput = { narrators: "J.K. Rowling" };
    const searchResult = baseSearchResult({
      narrators: [{ name: "J. K. Rowling" }],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "narrators").changed).toBe(false);
  });

  // Regression: the genre list is order-insensitive on the backend (MetadataRefreshDiffer sorts
  // before comparing), so a source that reports the same genres in a different order must not
  // be flagged here either - it used to compare the raw joined strings directly, so a "Genres"
  // change would show even though the backend's own diff (and its stored ChangedFieldsJson
  // badge) correctly considered it unchanged.
  it("does not flag a Genres change when only order differs", () => {
    const current: OrganizeAudiobookInput = { genres: "Fantasy/Adventure/Mystery" };
    const searchResult = baseSearchResult({
      genres: ["Mystery", "Fantasy", "Adventure"],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "genres").changed).toBe(false);
  });

  it("does not flag a Genres change when only duplication differs", () => {
    const current: OrganizeAudiobookInput = { genres: "Fantasy" };
    const searchResult = baseSearchResult({
      genres: ["Fantasy", "Fantasy"],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "genres").changed).toBe(false);
  });

  it("still flags a genuinely different Genres set", () => {
    const current: OrganizeAudiobookInput = { genres: "Fantasy/Adventure" };
    const searchResult = baseSearchResult({
      genres: ["Fantasy", "Horror"],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "genres").changed).toBe(true);
  });

  // Regression: sorting with `localeCompare(..., { sensitivity: "base" })` treats accent
  // variants as EQUAL (not just similar) for comparison purposes, so a set holding both "Cafe"
  // and "Café" ties under that comparator - and since Array#sort is stable, a tie preserves each
  // side's original input order, so the same two-element set reordered on one side could produce
  // a different joined comparable string than the other, wrongly flagging a change. The backend's
  // `StringComparer.OrdinalIgnoreCase` never ties on an accent difference, so this must sort the
  // same way (accent-sensitive) to agree with it regardless of input order.
  it("does not flag a Genres change for an accent-variant set reordered differently on each side", () => {
    const current: OrganizeAudiobookInput = { genres: "Café/Cafe" };
    const searchResult = baseSearchResult({
      genres: ["Cafe", "Café"],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "genres").changed).toBe(false);
  });

  // Regression: the backend diffs Authors/Narrators order-insensitively (MetadataRefreshDiffer's
  // JoinNames sorts before comparing), but this hook used to compare the raw joined strings, so
  // the same two authors reported in a different order than they're stored would show as an
  // Authors change here even though the backend's own diff considered it unchanged.
  it("does not flag an Authors change when only the order differs", () => {
    const current: OrganizeAudiobookInput = { authors: "Author B, Author A" };
    const searchResult = baseSearchResult({
      authors: [{ name: "Author A" }, { name: "Author B" }],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "authors").changed).toBe(false);
  });

  it("does not flag a Narrators change when only the order differs", () => {
    const current: OrganizeAudiobookInput = { narrators: "Narrator B, Narrator A" };
    const searchResult = baseSearchResult({
      narrators: [{ name: "Narrator A" }, { name: "Narrator B" }],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "narrators").changed).toBe(false);
  });

  // Regression: TagPreviewDialog's call site (BookEditForm.tsx) builds `currentInput.authors`
  // via organizeAudiobookInput's `joinList`, which uses " / " rather than PendingRefreshRowPanel's
  // ", "-joined `joinPersons` shape. Splitting only on "," left this call site entirely broken -
  // a multi-author value came through as one unsplit token, so Authors/Narrators showed changed
  // even when the actual sets matched exactly.
  it("does not flag an Authors change for a slash-joined current value (BookEditForm's shape)", () => {
    const current: OrganizeAudiobookInput = { authors: "Author A / Author B" };
    const searchResult = baseSearchResult({
      authors: [{ name: "Author B" }, { name: "Author A" }],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "authors").changed).toBe(false);
  });

  it("still flags a genuinely different Authors set despite order-insensitive comparison", () => {
    const current: OrganizeAudiobookInput = { authors: "Author A, Author B" };
    const searchResult = baseSearchResult({
      authors: [{ name: "Author A" }, { name: "Author C" }],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "authors").changed).toBe(true);
  });
});

describe("useMetadataFieldDiffs qualifiers", () => {
  const options = [
    { key: "abridged", label: "Abridged", suffix: " (Abridged)" },
    { key: "dramatized", label: "Dramatized", suffix: " (Dramatized)" },
  ];

  const diff = (current: OrganizeAudiobookInput, source: MetadataSearchResult) =>
    findField(
      renderHook(() => useMetadataFieldDiffs(current, source, [], false, undefined, options)).result
        .current,
      "qualifiers",
    );

  it("offers a qualifier the source reports and the book lacks", () => {
    const field = diff({}, baseSearchResult({ qualifiers: ["dramatized"] }));

    expect(field.changed).toBe(true);
    expect(field.currentValue).toBe("");
    expect(field.newValue).toBe("Dramatized");
  });

  it("proposes the union, so a qualifier the book already has is never dropped", () => {
    const field = diff(
      { qualifiers: ["abridged"] },
      baseSearchResult({ qualifiers: ["dramatized"] }),
    );

    expect(field.changed).toBe(true);
    expect(field.currentValue).toBe("Abridged");
    expect(field.newValue).toBe("Abridged, Dramatized");
  });

  it("is no change when the source reports none, even if the book has some", () => {
    const field = diff({ qualifiers: ["dramatized"] }, baseSearchResult());

    expect(field.changed).toBe(false);
    expect(field.newValue).toBe("Dramatized");
  });

  it("is no change when the book already has what the source reports", () => {
    const field = diff(
      { qualifiers: ["dramatized"] },
      baseSearchResult({ qualifiers: ["dramatized"] }),
    );

    expect(field.changed).toBe(false);
  });
});

describe("useMetadataFieldDiffs language default", () => {
  const languages = [
    { code: "en", displayName: "English", aliases: ["english"] },
    { code: "da", displayName: "Danish", aliases: ["dansk"] },
  ];
  const diff = (current: OrganizeAudiobookInput, sourceLanguage?: string, defaultLanguage = "en") =>
    findField(
      renderHook(() =>
        useMetadataFieldDiffs(
          current,
          baseSearchResult({ language: sourceLanguage }),
          languages,
          false,
          undefined,
          undefined,
          defaultLanguage,
        ),
      ).result.current,
      "language",
    );

  it("proposes English when neither the book nor the source has a language", () => {
    const field = diff({});

    expect(field.newValue).toBe("English");
    expect(field.changed).toBe(true);
  });

  it("keeps the book's own language when the source reports none", () => {
    const field = diff({ language: "da" });

    expect(field.newValue).toBe("Danish");
    expect(field.changed).toBe(false);
  });

  it("prefers the source's language over the default", () => {
    expect(diff({}, "Dansk").newValue).toBe("Danish");
  });
});
