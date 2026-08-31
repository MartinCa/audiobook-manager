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
});
