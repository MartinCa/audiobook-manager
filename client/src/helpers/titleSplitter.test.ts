import { describe, it, expect } from "vitest";
import { splitTitleOnColon } from "./titleSplitter";

describe("splitTitleOnColon", () => {
  it("returns the title unchanged when disabled", () => {
    const result = splitTitleOnColon("The Hobbit: There and Back Again", undefined, false);
    expect(result).toEqual({
      bookName: "The Hobbit: There and Back Again",
      subtitle: undefined,
    });
  });

  it("splits a genuine 'Title: Subtitle' title when enabled", () => {
    const result = splitTitleOnColon("The Hobbit: There and Back Again", undefined, true);
    expect(result).toEqual({
      bookName: "The Hobbit",
      subtitle: "There and Back Again",
    });
  });

  it("does not split a bare colon with no following space, even when enabled", () => {
    const result = splitTitleOnColon("4:50 from Paddington", undefined, true);
    expect(result).toEqual({
      bookName: "4:50 from Paddington",
      subtitle: undefined,
    });
  });

  it("never touches the title when a subtitle is already present", () => {
    const result = splitTitleOnColon(
      "The Hobbit: There and Back Again",
      "An Unexpected Subtitle",
      true,
    );
    expect(result).toEqual({
      bookName: "The Hobbit: There and Back Again",
      subtitle: "An Unexpected Subtitle",
    });
  });

  it("strips a subtitle already repeated at the end of the title when enabled", () => {
    const result = splitTitleOnColon(
      "Agatha Christie: A Very Elusive Woman",
      "A Very Elusive Woman",
      true,
    );
    expect(result).toEqual({
      bookName: "Agatha Christie",
      subtitle: "A Very Elusive Woman",
    });
  });

  it("leaves a title with a differing existing subtitle alone when enabled", () => {
    const result = splitTitleOnColon("Agatha Christie: A Very Elusive Woman", "Other", true);
    expect(result).toEqual({
      bookName: "Agatha Christie: A Very Elusive Woman",
      subtitle: "Other",
    });
  });
});
