import { describe, it, expect } from "vitest";
import { toAudiobook } from "./audiobookMapping";
import type { DiscoveredAudiobook } from "@/types/DiscoveredAudiobook";
import type { AudiobookDetail } from "@/types/AudiobookDetail";

describe("toAudiobook", () => {
  it("converts DiscoveredAudiobook with slash-separated lists properly", () => {
    const discovered: DiscoveredAudiobook = {
      fullPath: "/audiobooks/Author/Book/book.m4b",
      fileName: "book.m4b",
      sizeInBytes: 12345,
      bookName: "Test Book",
      subtitle: "A Subtitle",
      series: "Test Series",
      seriesPart: "1",
      year: 2024,
      authors: "Author One / Author Two",
      narrators: "Narrator One / Narrator Two",
      genres: "Sci-Fi / Fantasy",
      description: "Description here",
      copyright: "2024",
      publisher: "Publisher",
      language: "eng",
      rating: "5",
      asin: "B000TEST",
      www: "https://example.com",
      durationInSeconds: 3600,
      isWellTagged: true,
      isDuplicate: false,
    };

    const result = toAudiobook(discovered);
    expect(result.bookName).toBe("Test Book");
    expect(result.subtitle).toBe("A Subtitle");
    expect(result.authors).toEqual([{ name: "Author One" }, { name: "Author Two" }]);
    expect(result.narrators).toEqual([{ name: "Narrator One" }, { name: "Narrator Two" }]);
    expect(result.genres).toEqual(["Sci-Fi", "Fantasy"]);
    expect(result.durationInSeconds).toBe(3600);
    expect(result.fileInfo).toEqual({
      fullPath: "/audiobooks/Author/Book/book.m4b",
      fileName: "book.m4b",
      sizeInBytes: 12345,
    });
  });

  it("handles null / empty values on DiscoveredAudiobook gracefully", () => {
    const discovered: DiscoveredAudiobook = {
      fullPath: "/book.m4b",
      fileName: "book.m4b",
      sizeInBytes: 100,
      bookName: "",
      subtitle: null,
      series: null,
      seriesPart: null,
      year: null,
      authors: null,
      narrators: null,
      genres: null,
      description: null,
      copyright: null,
      publisher: null,
      language: null,
      rating: null,
      asin: null,
      www: null,
      durationInSeconds: null,
      isWellTagged: false,
      isDuplicate: false,
    };

    const result = toAudiobook(discovered);
    expect(result.bookName).toBeUndefined();
    expect(result.authors).toEqual([]);
    expect(result.narrators).toEqual([]);
    expect(result.genres).toEqual([]);
    expect(result.durationInSeconds).toBeUndefined();
    expect(result.fileInfo?.fullPath).toBe("/book.m4b");
  });

  it("converts AudiobookDetail with array properties properly", () => {
    const detail: AudiobookDetail = {
      id: 42,
      filePath: "/library/Book/book.m4b",
      fileName: "book.m4b",
      sizeInBytes: 54321,
      bookName: "Library Book",
      subtitle: "Library Subtitle",
      series: "Library Series",
      seriesPart: "2",
      year: 2023,
      authors: ["Author A", "Author B"],
      narrators: ["Narrator A"],
      genres: ["Adventure"],
      description: "Lib desc",
      copyright: "2023",
      publisher: "Lib pub",
      language: "en",
      rating: "4",
      asin: "B000LIB",
      www: "https://lib.com",
      coverFilePath: "/covers/42.jpg",
      durationInSeconds: 7200,
      authorRefs: [
        { id: 1, name: "Author A" },
        { id: 2, name: "Author B" },
      ],
    };

    const result = toAudiobook(detail);
    expect(result.bookName).toBe("Library Book");
    expect(result.authors).toEqual([{ name: "Author A" }, { name: "Author B" }]);
    expect(result.narrators).toEqual([{ name: "Narrator A" }]);
    expect(result.genres).toEqual(["Adventure"]);
    expect(result.durationInSeconds).toBe(7200);
    expect(result.fileInfo).toEqual({
      fullPath: "/library/Book/book.m4b",
      fileName: "book.m4b",
      sizeInBytes: 54321,
    });
  });

  it("carries the recorded split-title flag of an AudiobookDetail through", () => {
    const base = {
      id: 1,
      filePath: "/library/a.m4b",
      fileName: "a.m4b",
      sizeInBytes: 1,
      authors: ["A"],
      narrators: [],
      genres: [],
      authorRefs: [],
    };

    expect(toAudiobook({ ...base, splitTitleOnColon: true }).splitTitleOnColon).toBe(true);
    expect(toAudiobook(base).splitTitleOnColon).toBe(false);
  });

  it("carries the qualifiers of an AudiobookDetail through, alongside the clean name", () => {
    const result = toAudiobook({
      id: 1,
      filePath: "/library/a.m4b",
      fileName: "a.m4b",
      sizeInBytes: 1,
      bookName: "Killing Floor",
      series: "Jack Reacher",
      authors: ["Lee Child"],
      narrators: [],
      genres: [],
      authorRefs: [],
      qualifiers: ["abridged", "dramatized"],
    });

    expect(result.bookName).toBe("Killing Floor");
    expect(result.series).toBe("Jack Reacher");
    expect(result.qualifiers).toEqual(["abridged", "dramatized"]);
  });

  it("maps a book detail's additional series, and none for a discovered file", () => {
    const detail = {
      id: 7,
      bookName: "Mistborn",
      series: "Mistborn",
      seriesPart: "1",
      additionalSeries: [
        { seriesName: "Cosmere", seriesPart: "3" },
        { seriesName: "Standalone Universe", seriesPart: null },
      ],
      authors: ["Brandon Sanderson"],
      narrators: [],
      genres: [],
      filePath: "/library/a.m4b",
      fileName: "a.m4b",
      sizeInBytes: 1,
      authorRefs: [],
    } as AudiobookDetail;

    expect(toAudiobook(detail).additionalSeries).toEqual([
      { seriesName: "Cosmere", seriesPart: "3" },
      { seriesName: "Standalone Universe", seriesPart: undefined },
    ]);
    expect(toAudiobook({ ...detail, additionalSeries: undefined }).additionalSeries).toEqual([]);
  });
});
