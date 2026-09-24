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

  it("still flags a genuinely different Authors set despite order-insensitive comparison", () => {
    const current: OrganizeAudiobookInput = { authors: "Author A, Author B" };
    const searchResult = baseSearchResult({
      authors: [{ name: "Author A" }, { name: "Author C" }],
    });

    const { result } = renderHook(() => useMetadataFieldDiffs(current, searchResult, []));

    expect(findField(result.current, "authors").changed).toBe(true);
  });
});
