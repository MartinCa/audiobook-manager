import { describe, it, expect } from "vitest";
import { defaultSelectedKeys, isSelectedByDefault } from "./metadataApplyRules";
import type { InteractiveApplyRule, MetadataApplyRules } from "@/types/MetadataApplyRules";

function rulesFor(field: string, interactive: InteractiveApplyRule): MetadataApplyRules {
  return {
    fields: [
      {
        field,
        label: field,
        interactive,
        automated: "AskMe",
        alwaysOverwriteAllowed: true,
      },
    ],
    interactiveOptions: [],
    automatedOptions: [],
  };
}

const diff = (key: string, currentValue: string, newValue: string) => ({
  key,
  currentValue,
  newValue,
});

describe("isSelectedByDefault", () => {
  it("selects everything that changed when there are no rules", () => {
    expect(isSelectedByDefault(diff("description", "old", "new"), undefined)).toBe(true);
  });

  it("selects a field with no rule of its own, like the cover", () => {
    expect(
      isSelectedByDefault(
        diff("cover", "", "https://x/c.jpg"),
        rulesFor("Description", "NeverSelect"),
      ),
    ).toBe(true);
  });

  it.each<[InteractiveApplyRule, string, string, boolean]>([
    ["AlwaysSelect", "old", "new", true],
    ["AlwaysSelect", "", "", true],
    ["NeverSelect", "", "new", false],
    ["SelectIfEmpty", "", "new", true],
    ["SelectIfEmpty", "old", "new", false],
    ["SelectIfEmpty", "", "", false],
    ["SelectIfEmpty", "", "   ", false],
    ["SelectIfSourceHasValue", "old", "new", true],
    ["SelectIfSourceHasValue", "", "new", true],
    ["SelectIfSourceHasValue", "old", "", false],
  ])("%s with current %j and source %j is selected: %s", (rule, current, source, expected) => {
    expect(
      isSelectedByDefault(diff("description", current, source), rulesFor("Description", rule)),
    ).toBe(expected);
  });

  it("maps the client key to the backend field the rule is stored under", () => {
    // bookName -> BookName: a rule stored under the camelCase client key would never apply.
    expect(
      isSelectedByDefault(diff("bookName", "a", "b"), rulesFor("BookName", "NeverSelect")),
    ).toBe(false);
    expect(
      isSelectedByDefault(diff("bookName", "a", "b"), rulesFor("bookName", "NeverSelect")),
    ).toBe(true);
  });

  it("treats a year of 0 as empty", () => {
    expect(isSelectedByDefault(diff("year", "0", "2020"), rulesFor("Year", "SelectIfEmpty"))).toBe(
      true,
    );
    expect(
      isSelectedByDefault(diff("year", "2019", "2020"), rulesFor("Year", "SelectIfEmpty")),
    ).toBe(false);
  });
});

describe("defaultSelectedKeys", () => {
  it("returns only the keys whose rule selects them, in order", () => {
    const rules: MetadataApplyRules = {
      fields: [
        {
          field: "Description",
          label: "Description",
          interactive: "NeverSelect",
          automated: "AskMe",
          alwaysOverwriteAllowed: true,
        },
        {
          field: "Publisher",
          label: "Publisher",
          interactive: "SelectIfEmpty",
          automated: "AskMe",
          alwaysOverwriteAllowed: true,
        },
      ],
      interactiveOptions: [],
      automatedOptions: [],
    };

    expect(
      defaultSelectedKeys(
        [
          diff("description", "old", "new"),
          diff("publisher", "", "Acme"),
          diff("rating", "3", "4"),
        ],
        rules,
      ),
    ).toEqual(["publisher", "rating"]);
  });
});
