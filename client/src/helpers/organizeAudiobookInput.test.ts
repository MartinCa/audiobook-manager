import { describe, it, expect } from "vitest";
import { convertInputToAudiobook } from "./organizeAudiobookInput";
import OrganizeAudiobookInput from "../types/OrganizeAudiobookInput";

describe("convertInputToAudiobook", () => {
  it("splits authors/narrators/genres and trims whitespace", () => {
    const input: OrganizeAudiobookInput = {
      authors: "Author One,  Author Two",
      narrators: "Narrator One",
      genres: "Fantasy/Adventure",
      bookName: "The Book",
      year: 2020,
      rating: 4.5,
    };

    const result = convertInputToAudiobook(input, {
      durationInSeconds: 123,
      fileInfo: { fullPath: "/a/b.m4b", fileName: "b.m4b", sizeInBytes: 10 },
    });

    expect(result.authors).toEqual([
      { name: "Author One" },
      { name: "Author Two" },
    ]);
    expect(result.narrators).toEqual([{ name: "Narrator One" }]);
    expect(result.genres).toEqual(["Fantasy", "Adventure"]);
    expect(result.bookName).toBe("The Book");
    expect(result.rating).toBe("4.5");
    expect(result.durationInSeconds).toBe(123);
    expect(result.fileInfo).toEqual({
      fullPath: "/a/b.m4b",
      fileName: "b.m4b",
      sizeInBytes: 10,
    });
  });

  it("only builds a cover when both base64 and mime type are present", () => {
    const withCover = convertInputToAudiobook(
      { cover_base64: "abc", cover_mime: "image/jpeg" },
      {},
    );
    expect(withCover.cover).toEqual({
      base64Data: "abc",
      mimeType: "image/jpeg",
    });

    const withoutCover = convertInputToAudiobook(
      { cover_base64: "abc", cover_mime: undefined },
      {},
    );
    expect(withoutCover.cover).toBeUndefined();
  });

  it("defaults authors/narrators/genres to empty arrays when unset", () => {
    const result = convertInputToAudiobook({}, {});
    expect(result.authors).toEqual([]);
    expect(result.narrators).toEqual([]);
    expect(result.genres).toEqual([]);
  });
});

describe("convertInputToAudiobook blank-field handling", () => {
  const meta = {
    durationInSeconds: 100,
    fileInfo: {
      fullPath: "/library/book.m4b",
      fileName: "book.m4b",
      sizeInBytes: 1,
    },
  };

  it("maps an empty genres field to no genres rather than one blank genre", () => {
    // Regression: "".split("/") yields [""], and the backend persisted that as a real Genre row
    // with an empty name that every genre-less book then linked to.
    const result = convertInputToAudiobook(
      { bookName: "A Book", genres: "" },
      meta,
    );

    expect(result.genres).toEqual([]);
  });

  it("maps an undefined genres field to no genres", () => {
    const result = convertInputToAudiobook({ bookName: "A Book" }, meta);

    expect(result.genres).toEqual([]);
  });

  it("drops blank entries between separators and trims the rest", () => {
    const result = convertInputToAudiobook(
      { bookName: "A Book", genres: " Fantasy / / Adventure " },
      meta,
    );

    expect(result.genres).toEqual(["Fantasy", "Adventure"]);
  });

  it("maps an empty authors field to no authors rather than one blank author", () => {
    const result = convertInputToAudiobook(
      { bookName: "A Book", authors: "" },
      meta,
    );

    expect(result.authors).toEqual([]);
  });

  it("drops blank author entries and trims the rest", () => {
    const result = convertInputToAudiobook(
      { bookName: "A Book", authors: "Author One, , Author Two " },
      meta,
    );

    expect(result.authors).toEqual([
      { name: "Author One" },
      { name: "Author Two" },
    ]);
  });

  it("drops blank narrator entries", () => {
    const result = convertInputToAudiobook(
      { bookName: "A Book", narrators: " , Narrator One" },
      meta,
    );

    expect(result.narrators).toEqual([{ name: "Narrator One" }]);
  });
});
