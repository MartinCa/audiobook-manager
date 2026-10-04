import type { FieldDiff } from "@/hooks/useMetadataFieldDiffs";
import type { MetadataApplyRules } from "@/types/MetadataApplyRules";

/**
 * The client's diff keys (see `useMetadataFieldDiffs`) against the backend's `MetadataRefreshFields`
 * names the apply rules are keyed by. A key without an entry (the cover) has no rule and keeps the
 * default: selected whenever it changed.
 */
export const CLIENT_KEY_TO_BACKEND_FIELDS: Record<string, string[]> = {
  authors: ["Authors"],
  narrators: ["Narrators"],
  bookName: ["BookName"],
  subtitle: ["Subtitle"],
  series: ["Series"],
  year: ["Year"],
  genres: ["Genres"],
  description: ["Description"],
  rating: ["Rating"],
  publisher: ["Publisher"],
  language: ["Language"],
  copyright: ["Copyright"],
  asin: ["Asin"],
  www: ["Www"],
  qualifiers: ["Qualifiers"],
};

/** "Empty" as the server's `MetadataApplyRuleSet.IsEmpty` defines it: no text, or a year of 0. */
function isEmpty(key: string, value: string | null | undefined): boolean {
  const text = value?.trim() ?? "";
  return text === "" || (key === "year" && text === "0");
}

/**
 * Whether a review starts with this changed field ticked, per its "when reviewing" rule. This is the
 * client twin of the server's `MetadataApplyRuleSet.IsSelectedByDefault`: the review dialogs compute
 * their own diffs, so the (four-case) evaluation is mirrored here while the field and option lists
 * themselves come from the server. With no rules loaded - or none for the field - everything that
 * changed is selected, which is how reviews behaved before the rules existed.
 */
export function isSelectedByDefault(
  diff: Pick<FieldDiff, "key" | "currentValue" | "newValue">,
  rules: MetadataApplyRules | undefined,
): boolean {
  const backendField = CLIENT_KEY_TO_BACKEND_FIELDS[diff.key]?.[0];
  const rule = rules?.fields.find((f) => f.field === backendField);
  if (!rule) return true;

  const currentEmpty = isEmpty(diff.key, diff.currentValue);
  const sourceEmpty = isEmpty(diff.key, diff.newValue);
  switch (rule.interactive) {
    case "NeverSelect":
      return false;
    case "SelectIfEmpty":
      return currentEmpty && !sourceEmpty;
    case "SelectIfSourceHasValue":
      return !sourceEmpty;
    default:
      return true;
  }
}

/** The keys of the changed fields a review starts with ticked. */
export function defaultSelectedKeys(
  changedFields: readonly Pick<FieldDiff, "key" | "currentValue" | "newValue">[],
  rules: MetadataApplyRules | undefined,
): string[] {
  return changedFields.filter((f) => isSelectedByDefault(f, rules)).map((f) => f.key);
}
