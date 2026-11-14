import { describe, it, expect } from "vitest";
import { canChooseMainAuthor, keepMainAuthor, mainAuthorOf, NO_MAIN_AUTHOR } from "./mainAuthor";

describe("mainAuthorOf", () => {
  it("is the main author while it is still one of the authors", () => {
    expect(mainAuthorOf({ authors: ["A One", "B Two"], mainAuthor: "B Two" })).toBe("B Two");
  });

  it("is undefined when the main author is no longer among the authors", () => {
    expect(mainAuthorOf({ authors: ["A One"], mainAuthor: "B Two" })).toBeUndefined();
  });

  it("matches the name exactly - the backend keys people by exact name", () => {
    expect(mainAuthorOf({ authors: ["A One"], mainAuthor: "a one" })).toBeUndefined();
  });

  it.each([undefined, "", "   "])("treats %j as no main author", (mainAuthor) => {
    expect(mainAuthorOf({ authors: ["A One"], mainAuthor })).toBeUndefined();
  });

  it("trims the stored name before matching", () => {
    expect(mainAuthorOf({ authors: ["A One"], mainAuthor: " A One " })).toBe("A One");
  });

  it("copes with missing authors and a missing object", () => {
    expect(mainAuthorOf({ mainAuthor: "A One" })).toBeUndefined();
    expect(mainAuthorOf(undefined)).toBeUndefined();
  });
});

describe("keepMainAuthor", () => {
  it("keeps the name while it is still an author, and otherwise returns the empty form-state value", () => {
    expect(keepMainAuthor("A One", ["A One", "B Two"])).toBe("A One");
    expect(keepMainAuthor("A One", ["B Two"])).toBe("");
    expect(keepMainAuthor(undefined, ["B Two"])).toBe("");
  });
});

describe("canChooseMainAuthor", () => {
  it("is offered once there are several authors to pick from", () => {
    expect(canChooseMainAuthor(["A One", "B Two"], undefined)).toBe(true);
  });

  it("is not offered for a single author with none chosen", () => {
    expect(canChooseMainAuthor(["A One"], undefined)).toBe(false);
  });

  it("stays offered for a single author when one is already chosen, so it can be cleared", () => {
    expect(canChooseMainAuthor(["A One"], "A One")).toBe(true);
  });

  it("does not count blank entries as authors", () => {
    expect(canChooseMainAuthor(["A One", "  ", ""], undefined)).toBe(false);
  });
});

describe("NO_MAIN_AUTHOR", () => {
  it("is not something an author could be called", () => {
    expect(NO_MAIN_AUTHOR.startsWith("__")).toBe(true);
  });
});
