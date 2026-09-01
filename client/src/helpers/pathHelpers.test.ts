import { describe, it, expect } from "vitest";
import { pathsEqual } from "./pathHelpers";

describe("pathsEqual", () => {
  it("returns true for exact matches", () => {
    expect(pathsEqual("/path/to/book", "/path/to/book")).toBe(true);
  });

  it("handles backslash and forward slash differences", () => {
    expect(pathsEqual("C:\\Audiobooks\\Book.m4b", "C:/Audiobooks/Book.m4b")).toBe(true);
  });

  it("is case-insensitive", () => {
    expect(pathsEqual("/Path/To/Book.m4b", "/path/to/book.m4b")).toBe(true);
  });

  it("ignores trailing slashes", () => {
    expect(pathsEqual("/path/to/dir/", "/path/to/dir")).toBe(true);
    expect(pathsEqual("C:\\path\\to\\dir\\\\", "C:/path/to/dir/")).toBe(true);
  });

  it("returns false for different paths", () => {
    expect(pathsEqual("/path/to/book1.m4b", "/path/to/book2.m4b")).toBe(false);
  });

  it("handles null or undefined correctly", () => {
    expect(pathsEqual(null, null)).toBe(true);
    expect(pathsEqual(undefined, undefined)).toBe(true);
    expect(pathsEqual(null, undefined)).toBe(true);
    expect(pathsEqual("/path/to/book", null)).toBe(false);
    expect(pathsEqual(undefined, "/path/to/book")).toBe(false);
  });
});
