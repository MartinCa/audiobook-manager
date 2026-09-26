import { describe, it, expect } from "vitest";
import { buildDefaultMetadataSearchQuery } from "./metadataSearchQuery";

describe("buildDefaultMetadataSearchQuery", () => {
  it("prefers author(s) and book name when both are present", () => {
    expect(
      buildDefaultMetadataSearchQuery(["Brandon Sanderson"], "The Way of Kings", "file.m4b"),
    ).toBe("Brandon Sanderson - The Way of Kings");
  });

  it("joins multiple authors with a comma", () => {
    expect(
      buildDefaultMetadataSearchQuery(["Author One", "Author Two"], "Book Title", "file.m4b"),
    ).toBe("Author One, Author Two - Book Title");
  });

  it("falls back to book name alone when there are no authors", () => {
    expect(buildDefaultMetadataSearchQuery([], "The Way of Kings", "file.m4b")).toBe(
      "The Way of Kings",
    );
  });

  it("falls back to book name alone when authors is undefined", () => {
    expect(buildDefaultMetadataSearchQuery(undefined, "The Way of Kings", "file.m4b")).toBe(
      "The Way of Kings",
    );
  });

  it("falls back to book name when authors are only blank strings", () => {
    expect(buildDefaultMetadataSearchQuery(["  ", ""], "The Way of Kings", "file.m4b")).toBe(
      "The Way of Kings",
    );
  });

  it("falls back to file name when both authors and book name are missing", () => {
    expect(buildDefaultMetadataSearchQuery([], "", "some-file.m4b")).toBe("some-file.m4b");
  });

  it("falls back to file name when book name is only whitespace, even with an author", () => {
    expect(buildDefaultMetadataSearchQuery(["Author"], "   ", "some-file.m4b")).toBe(
      "some-file.m4b",
    );
  });

  it("returns an empty string when nothing is available", () => {
    expect(buildDefaultMetadataSearchQuery([], "", undefined)).toBe("");
  });
});
