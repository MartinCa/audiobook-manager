import { describe, expect, it } from "vitest";
import { isAbsoluteHttpUrl } from "./urlHelpers";

describe("isAbsoluteHttpUrl", () => {
  it("accepts an absolute https URL", () => {
    expect(isAbsoluteHttpUrl("https://hardcover.app/books/1984")).toBe(true);
  });

  it("accepts an absolute http URL", () => {
    expect(isAbsoluteHttpUrl("http://www.audible.com/pd/1")).toBe(true);
  });

  it("rejects a plain search query", () => {
    expect(isAbsoluteHttpUrl("nineteen eighty-four")).toBe(false);
  });

  it("rejects an empty string", () => {
    expect(isAbsoluteHttpUrl("")).toBe(false);
  });

  it("rejects a non-http(s) scheme", () => {
    expect(isAbsoluteHttpUrl("ftp://example.com/book")).toBe(false);
  });

  it("rejects a query that merely contains a URL-like substring", () => {
    expect(isAbsoluteHttpUrl("see https://hardcover.app/books/1984 for details")).toBe(false);
  });
});
