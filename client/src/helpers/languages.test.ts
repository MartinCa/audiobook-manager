import { describe, it, expect } from "vitest";
import { normalizeLanguage, languageLabel, languageSelectItems } from "./languages";

describe("languages helper", () => {
  const mockLangs = [
    { code: "en", name: "English" },
    { code: "da", name: "Danish" },
  ];

  it("normalizes language inputs", () => {
    expect(normalizeLanguage("en", mockLangs)).toBe("en");
    expect(normalizeLanguage("English", mockLangs)).toBe("en");
    expect(normalizeLanguage("UNKNOWN", mockLangs)).toBe("UNKNOWN");
    expect(normalizeLanguage("", mockLangs)).toBe("");
  });

  it("gets language labels", () => {
    expect(languageLabel("en", mockLangs)).toBe("English");
    expect(languageLabel("custom", mockLangs)).toBe("custom");
  });

  it("builds select items with unrecognized fallback", () => {
    const items = languageSelectItems("fr", mockLangs);
    expect(items).toHaveLength(3);
    expect(items[2]).toEqual({ code: "fr", name: "fr (unrecognized)" });
  });
});
