import { describe, it, expect } from "vitest";
import { splitList, joinList, convertInputToAudiobook } from "./organizeAudiobookInput";

describe("organizeAudiobookInput helper", () => {
  it("splits lists correctly", () => {
    expect(splitList("Author 1 / Author 2")).toEqual(["Author 1", "Author 2"]);
    expect(splitList("")).toEqual([]);
    expect(splitList(null)).toEqual([]);
  });

  it("joins lists correctly", () => {
    expect(joinList(["Author 1", "Author 2"])).toBe("Author 1 / Author 2");
    expect(joinList([])).toBe("");
  });

  it("converts form input to audiobook object", () => {
    const res = convertInputToAudiobook(
      {
        bookName: "Test Book",
        authors: "Author A / Author B",
        narrators: "Narrator A",
        genres: "Fantasy",
        year: 2023,
      },
      "/path/to/book.m4b"
    );

    expect(res.bookName).toBe("Test Book");
    expect(res.authors).toEqual(["Author A", "Author B"]);
    expect(res.narrators).toEqual(["Narrator A"]);
    expect(res.genres).toEqual(["Fantasy"]);
    expect(res.year).toBe(2023);
    expect(res.fullPath).toBe("/path/to/book.m4b");
  });
});
