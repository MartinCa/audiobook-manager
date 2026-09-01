import { describe, it, expect } from "vitest";
import { joinPersons } from "./bookDetailsHelpers";
import type { AudiobookPerson } from "../types/Audiobook";

describe("joinPersons", () => {
  it("joins multiple persons with a comma and space", () => {
    const persons: AudiobookPerson[] = [{ name: "Brandon Sanderson" }, { name: "Michael Kramer" }];
    expect(joinPersons(persons)).toBe("Brandon Sanderson, Michael Kramer");
  });

  it("returns a single name unchanged when there is only one person", () => {
    expect(joinPersons([{ name: "Brandon Sanderson" }])).toBe("Brandon Sanderson");
  });

  it("returns an empty string for an empty array", () => {
    expect(joinPersons([])).toBe("");
  });

  it("ignores the role field and only joins names", () => {
    const persons: AudiobookPerson[] = [
      { name: "Brandon Sanderson", role: "Author" },
      { name: "Michael Kramer", role: "Narrator" },
    ];
    expect(joinPersons(persons)).toBe("Brandon Sanderson, Michael Kramer");
  });
});
