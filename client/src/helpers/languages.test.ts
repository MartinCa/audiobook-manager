import { describe, it, expect } from "vitest";
import { languageLabel, languageSelectItems, normalizeLanguage } from "./languages";
import type { LanguageOption } from "@/types/Language";

const languages: LanguageOption[] = [
  { code: "en", displayName: "English", aliases: ["en", "eng", "english"] },
  {
    code: "da",
    displayName: "Danish",
    aliases: ["da", "dan", "danish", "dansk"],
  },
];

describe("normalizeLanguage", () => {
  it.each(["en", "EN", "eng", "English", "english", "  English  ", "en-US", "en_GB"])(
    "folds %s to en",
    (raw) => {
      expect(normalizeLanguage(raw, languages)).toBe("en");
    },
  );

  it.each(["da", "DA", "dan", "Danish", "da-DK"])("folds %s to da", (raw) => {
    expect(normalizeLanguage(raw, languages)).toBe("da");
  });

  it.each(["German", "de", "xx", "Swedish"])(
    "returns undefined for %s, a language the library does not manage",
    (raw) => {
      expect(normalizeLanguage(raw, languages)).toBeUndefined();
    },
  );

  it.each(["", "   "])("returns undefined for an empty value", (raw) => {
    expect(normalizeLanguage(raw, languages)).toBeUndefined();
  });

  it("returns undefined for null and undefined", () => {
    expect(normalizeLanguage(null, languages)).toBeUndefined();
    expect(normalizeLanguage(undefined, languages)).toBeUndefined();
  });

  it("returns undefined before the list has been fetched", () => {
    expect(normalizeLanguage("English", [])).toBeUndefined();
  });
});

describe("languageLabel", () => {
  it("shows the display name for a managed code", () => {
    expect(languageLabel("en", languages)).toBe("English");
    expect(languageLabel("da", languages)).toBe("Danish");
  });

  it("falls back to the raw value for an unmanaged code", () => {
    expect(languageLabel("de", languages)).toBe("de");
  });

  it("renders nothing for an empty value", () => {
    expect(languageLabel(undefined, languages)).toBe("");
    expect(languageLabel("", languages)).toBe("");
  });
});

describe("languageSelectItems", () => {
  it("offers just the managed languages for a supported value", () => {
    expect(languageSelectItems("en", languages)).toEqual(languages);
  });

  it("offers just the managed languages when nothing is selected", () => {
    expect(languageSelectItems(undefined, languages)).toEqual(languages);
    expect(languageSelectItems("", languages)).toEqual(languages);
  });

  it("keeps an unmanaged current value as an option so it survives an unrelated edit", () => {
    expect(languageSelectItems("de", languages)).toEqual([
      { code: "en", displayName: "English", aliases: ["en", "eng", "english"] },
      {
        code: "da",
        displayName: "Danish",
        aliases: ["da", "dan", "danish", "dansk"],
      },
      { code: "de", displayName: "de (unrecognized)", aliases: [] },
    ]);
  });
});
