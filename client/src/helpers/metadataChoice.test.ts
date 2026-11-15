import { describe, expect, it } from "vitest";
import {
  choiceRulesFrom,
  DEFAULT_CHOICE_RULE,
  normalizeName,
  resolveMainAuthor,
  resolveMetadataChoices,
  resolvePrimarySeries,
  sameName,
  DEFAULT_CHOICE_RULES,
} from "./metadataChoice";
import type {
  ChoiceRule,
  MetadataChoiceRules,
  SourceNamesMainRule,
  SourceSilentRule,
} from "@/types/MetadataChoiceRules";

// The same cases as MetadataChoiceRuleSetTests (C#): the review dialogs preselect with this twin and
// the server applies unattended with the original, so the two must answer identically.

const rule = (
  named: SourceNamesMainRule = "KeepCurrent",
  silent: SourceSilentRule = "KeepCurrent",
): ChoiceRule => ({ whenSourceNamesMain: named, whenSourceSilent: silent });

const TWO_AUTHORS = ["A One", "B Two"];
const TWO_SERIES = ["Saga", "Cosmere"];
const out = (value: string | undefined, needsAsk: boolean) => ({ value, needsAsk });

describe("resolveMainAuthor", () => {
  it("keeps the current one by default, whether or not the source names one", () => {
    expect(resolveMainAuthor(DEFAULT_CHOICE_RULE, TWO_AUTHORS, "A One", "B Two")).toEqual(
      out("B Two", false),
    );
    expect(resolveMainAuthor(DEFAULT_CHOICE_RULE, TWO_AUTHORS, undefined, "B Two")).toEqual(
      out("B Two", false),
    );
    expect(resolveMainAuthor(DEFAULT_CHOICE_RULE, TWO_AUTHORS, "A One", undefined)).toEqual(
      out(undefined, false),
    );
  });

  it("drops a current main author the source no longer credits", () => {
    expect(resolveMainAuthor(DEFAULT_CHOICE_RULE, TWO_AUTHORS, undefined, "Gone Author")).toEqual(
      out(undefined, false),
    );
  });

  it("has nothing to choose with fewer than two source authors - no rule sets one", () => {
    const r = rule("UseSources", "FirstListed");
    expect(resolveMainAuthor(r, ["A One"], "A One", undefined)).toEqual(out(undefined, false));
    expect(resolveMainAuthor(r, ["A One"], undefined, "A One")).toEqual(out("A One", false));
    expect(resolveMainAuthor(r, [], undefined, "A One")).toEqual(out(undefined, false));
  });

  it("lets an explicit pick beat every rule, and an empty pick mean no main author", () => {
    const ask = rule("AskMe", "AskMe");
    expect(resolveMainAuthor(ask, TWO_AUTHORS, "A One", "A One", "B Two")).toEqual(
      out("B Two", false),
    );
    expect(resolveMainAuthor(DEFAULT_CHOICE_RULE, TWO_AUTHORS, "A One", "B Two", "")).toEqual(
      out(undefined, false),
    );
  });

  it("ignores an explicit pick of someone the source does not list", () => {
    expect(
      resolveMainAuthor(DEFAULT_CHOICE_RULE, TWO_AUTHORS, undefined, "B Two", "Nobody"),
    ).toEqual(out("B Two", false));
  });

  it("takes the source's main author when the rule is Use the source's", () => {
    const r = rule("UseSources");
    expect(resolveMainAuthor(r, TWO_AUTHORS, "A One", "B Two")).toEqual(out("A One", false));
    expect(resolveMainAuthor(r, TWO_AUTHORS, "A One", undefined)).toEqual(out("A One", false));
  });

  it("asks, when the source names one, only if it differs from the current one", () => {
    const r = rule("AskMe");
    expect(resolveMainAuthor(r, TWO_AUTHORS, "A One", "B Two")).toEqual(out("B Two", true));
    expect(resolveMainAuthor(r, TWO_AUTHORS, "A One", undefined)).toEqual(out(undefined, true));
    expect(resolveMainAuthor(r, TWO_AUTHORS, "A One", "A One")).toEqual(out("A One", false));
  });

  it("ignores the source's main author when the rule is Keep current", () => {
    expect(resolveMainAuthor(rule("KeepCurrent"), TWO_AUTHORS, "A One", "B Two")).toEqual(
      out("B Two", false),
    );
  });

  it("fills a gap but never replaces the current one when silent and First listed", () => {
    const r = rule("KeepCurrent", "FirstListed");
    expect(resolveMainAuthor(r, TWO_AUTHORS, undefined, undefined)).toEqual(out("A One", false));
    expect(resolveMainAuthor(r, TWO_AUTHORS, undefined, "B Two")).toEqual(out("B Two", false));
    expect(resolveMainAuthor(r, TWO_AUTHORS, undefined, "Gone Author")).toEqual(
      out("A One", false),
    );
  });

  it("clears the main author when silent and the rule is No main author", () => {
    expect(resolveMainAuthor(rule("KeepCurrent", "None"), TWO_AUTHORS, undefined, "B Two")).toEqual(
      out(undefined, false),
    );
  });

  it("asks, when silent, only if the book would be left with none", () => {
    const r = rule("KeepCurrent", "AskMe");
    expect(resolveMainAuthor(r, TWO_AUTHORS, undefined, "B Two")).toEqual(out("B Two", false));
    expect(resolveMainAuthor(r, TWO_AUTHORS, undefined, undefined)).toEqual(out(undefined, true));
    expect(resolveMainAuthor(r, TWO_AUTHORS, undefined, "Gone Author")).toEqual(
      out(undefined, true),
    );
  });

  it("applies the rule that matches whether the source names a main author", () => {
    const r = rule("UseSources", "KeepCurrent");
    expect(resolveMainAuthor(r, TWO_AUTHORS, "A One", "B Two").value).toBe("A One");
    expect(resolveMainAuthor(r, TWO_AUTHORS, undefined, "B Two").value).toBe("B Two");
  });

  it("compares names across initials spellings and returns the source's spelling", () => {
    expect(
      resolveMainAuthor(
        rule("KeepCurrent"),
        ["George R.R. Martin", "Other Person"],
        undefined,
        "George R. R. Martin",
      ),
    ).toEqual(out("George R.R. Martin", false));
  });

  it("does not treat blank or repeated source names as candidates", () => {
    expect(
      resolveMainAuthor(
        rule("KeepCurrent", "FirstListed"),
        ["A One", "a one", "  ", null],
        undefined,
        undefined,
      ),
    ).toEqual(out(undefined, false));
  });
});

describe("resolvePrimarySeries", () => {
  it("leaves the choice to the series default (no explicit primary)", () => {
    expect(resolvePrimarySeries(DEFAULT_CHOICE_RULE, TWO_SERIES, "Cosmere", ["Saga"])).toEqual(
      out(undefined, false),
    );
    expect(resolvePrimarySeries(DEFAULT_CHOICE_RULE, TWO_SERIES, undefined, [])).toEqual(
      out(undefined, false),
    );
  });

  it("lets an explicit pick win", () => {
    expect(
      resolvePrimarySeries(rule("AskMe", "AskMe"), TWO_SERIES, "Saga", ["Saga"], "Cosmere"),
    ).toEqual(out("Cosmere", false));
  });

  it("has nothing to choose with fewer than two series", () => {
    expect(
      resolvePrimarySeries(rule("UseSources", "AskMe"), ["Saga"], "Saga", ["Cosmere"]),
    ).toEqual(out(undefined, false));
  });

  it("takes the source's featured series when the rule is Use the source's", () => {
    expect(resolvePrimarySeries(rule("UseSources"), TWO_SERIES, "Cosmere", ["Saga"])).toEqual(
      out("Cosmere", false),
    );
  });

  it("ignores the source's featured series when the rule is Keep current", () => {
    expect(resolvePrimarySeries(rule("KeepCurrent"), TWO_SERIES, "Cosmere", ["Saga"])).toEqual(
      out(undefined, false),
    );
  });

  it("asks, when the source names one, only if it differs from the current primary", () => {
    const r = rule("AskMe");
    expect(resolvePrimarySeries(r, TWO_SERIES, "Cosmere", ["Saga"])).toEqual(out(undefined, true));
    expect(resolvePrimarySeries(r, TWO_SERIES, "Saga", ["Saga"])).toEqual(out(undefined, false));
  });

  it("takes the first listed only when none of the book's series survives (source order)", () => {
    const r = rule("KeepCurrent", "FirstListed");
    expect(resolvePrimarySeries(r, TWO_SERIES, undefined, ["Cosmere"])).toEqual(
      out(undefined, false),
    );
    expect(resolvePrimarySeries(r, TWO_SERIES, undefined, ["Elsewhere"])).toEqual(
      out("Saga", false),
    );
    expect(resolvePrimarySeries(r, TWO_SERIES, undefined, [])).toEqual(out("Saga", false));
  });

  it("asks, when silent, only if none of the book's series survives", () => {
    const r = rule("KeepCurrent", "AskMe");
    expect(resolvePrimarySeries(r, TWO_SERIES, undefined, ["Saga"])).toEqual(out(undefined, false));
    expect(resolvePrimarySeries(r, TWO_SERIES, undefined, ["Elsewhere"])).toEqual(
      out(undefined, true),
    );
  });
});

describe("normalizeName / sameName", () => {
  it.each([
    ["George R. R. Martin", "george rr martin"],
    ["George R.R. Martin", "george rr martin"],
    ["Fantasy & Adventure", "fantasy and adventure"],
    ["  A   B  Smith", "ab smith"],
    ["", ""],
  ])("normalizes %j to %j", (input, expected) => {
    expect(normalizeName(input)).toBe(expected);
  });

  it("never treats blank names as the same", () => {
    expect(sameName("", "")).toBe(false);
    expect(sameName(undefined, null)).toBe(false);
  });
});

describe("choiceRulesFrom", () => {
  it("is all defaults while the rules are not loaded", () => {
    expect(choiceRulesFrom(undefined)).toEqual(DEFAULT_CHOICE_RULES);
  });

  it("reads the two settings of each served choice", () => {
    const served: MetadataChoiceRules = {
      choices: [
        {
          choice: "MainAuthor",
          label: "Main author",
          description: "",
          whenSourceNamesMain: "UseSources",
          whenSourceSilent: "None",
          namesMainOptions: [],
          silentOptions: [],
        },
      ],
    };
    expect(choiceRulesFrom(served)).toEqual({
      mainAuthor: { whenSourceNamesMain: "UseSources", whenSourceSilent: "None" },
      primarySeries: DEFAULT_CHOICE_RULE,
    });
  });
});

describe("resolveMetadataChoices", () => {
  const person = (name: string) => ({ name });
  const current = { authors: "A One, B Two", mainAuthor: "B Two", series: "Saga" };

  it("resolves both choices for one source result", () => {
    const choices = resolveMetadataChoices(
      { mainAuthor: rule("UseSources"), primarySeries: rule("UseSources") },
      current,
      {
        authors: [person("A One"), person("B Two")],
        mainAuthor: "A One",
        series: [{ seriesName: "Saga" }, { seriesName: "Cosmere" }],
        mainSeries: "Cosmere",
      },
    );
    expect(choices).toMatchObject({
      authorOptions: ["A One", "B Two"],
      currentMainAuthor: "B Two",
      mainAuthor: "A One",
      mainAuthorNeedsAsk: false,
      primarySeries: "Cosmere",
      primarySeriesNeedsAsk: false,
    });
  });

  it("reports an explicit empty pick as no main author, shown as an empty string", () => {
    const choices = resolveMetadataChoices(
      DEFAULT_CHOICE_RULES,
      current,
      { authors: [person("A One"), person("B Two")], series: [] },
      "",
    );
    expect(choices.mainAuthor).toBe("");
  });

  it("ignores a current main author that is no longer one of the book's authors", () => {
    const choices = resolveMetadataChoices(
      DEFAULT_CHOICE_RULES,
      { authors: "A One", mainAuthor: "B Two" },
      { authors: [person("A One"), person("B Two")], series: [] },
    );
    expect(choices.currentMainAuthor).toBe("");
    expect(choices.mainAuthor).toBe("");
  });
});
