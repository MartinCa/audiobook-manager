import { describe, it, expect } from "vitest";
import { pendingSnapshotToSearchResult } from "@/helpers/pendingMetadataRefresh";

const snapshot = {
  url: "https://example.com/book/clean",
  source: "Goodreads",
  authors: ["Brandon Sanderson"],
  narrators: ["Michael Kramer"],
  bookName: "The Way of Kings",
  subtitle: "Book One",
  seriesName: "The Stormlight Archive",
  seriesPart: "1",
  year: 2010,
  genres: ["Epic Fantasy"],
  description: "A story of kings.",
  language: "eng",
  rating: "4.82",
  copyright: "© 2010",
  publisher: "Tor",
  asin: "B003P2WO5E",
};

describe("pendingSnapshotToSearchResult", () => {
  it("maps the snapshot onto the MetadataSearchResult shape the preview UI renders", () => {
    const result = pendingSnapshotToSearchResult(snapshot);
    expect(result).toEqual({
      url: snapshot.url,
      cleanUrl: snapshot.url,
      source: snapshot.source,
      authors: [{ name: "Brandon Sanderson" }],
      narrators: [{ name: "Michael Kramer" }],
      bookName: "The Way of Kings",
      subtitle: "Book One",
      year: 2010,
      genres: ["Epic Fantasy"],
      description: "A story of kings.",
      language: "eng",
      rating: 4.82,
      copyright: "© 2010",
      publisher: "Tor",
      asin: "B003P2WO5E",
      series: [{ seriesName: "The Stormlight Archive", seriesPart: "1" }],
    });
  });

  it("handles a snapshot with mostly-empty optional fields", () => {
    const sparse = {
      ...snapshot,
      subtitle: undefined,
      seriesName: undefined,
      rating: "0",
      year: null,
    };
    const result = pendingSnapshotToSearchResult(sparse);
    expect(result.subtitle).toBeUndefined();
    expect(result.series).toEqual([]);
    expect(result.year).toBeUndefined();
    // `rating: "0"` is truthy, so it converts to the number 0 rather than dropping to undefined.
    expect(result.rating).toBe(0);
  });

  it("treats absent collection fields as empty rather than failing", () => {
    const none = {
      url: "https://example.com/book/clean",
      source: "Goodreads",
      authors: undefined,
      narrators: undefined,
      bookName: undefined,
      genres: undefined,
    };
    const result = pendingSnapshotToSearchResult(none);
    expect(result.authors).toEqual([]);
    expect(result.narrators).toEqual([]);
    expect(result.genres).toEqual([]);
    expect(result.bookName).toBe("");
  });
});
