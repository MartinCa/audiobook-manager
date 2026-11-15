import type {
  ChoiceRule,
  MetadataChoiceRules,
  SourceNamesMainRule,
  SourceSilentRule,
} from "@/types/MetadataChoiceRules";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";

/**
 * The client twin of `MetadataChoiceRuleSet`'s resolution (the server is the source of truth and
 * runs it for every unattended apply). A review dialog computes its own diff and picker, so it needs
 * the same answer to pre-select the same author or series the server would pick - kept in step by
 * `metadataChoice.test.ts`, which pins the same cases as `MetadataChoiceRuleSetTests`.
 *
 * It decides which of a source's several authors is the book's main author and which of its several
 * series is the primary one, depending on whether the source itself names a main one.
 */

export const MAIN_AUTHOR_CHOICE = "MainAuthor";
export const PRIMARY_SERIES_CHOICE = "PrimarySeries";

export const DEFAULT_CHOICE_RULE: ChoiceRule = {
  whenSourceNamesMain: "KeepCurrent",
  whenSourceSilent: "KeepCurrent",
};

/** What each choice does, by choice key; a missing one is the default (keep the current). */
export interface ChoiceRules {
  mainAuthor: ChoiceRule;
  primarySeries: ChoiceRule;
}

export const DEFAULT_CHOICE_RULES: ChoiceRules = {
  mainAuthor: DEFAULT_CHOICE_RULE,
  primarySeries: DEFAULT_CHOICE_RULE,
};

/** The served rules as the two settings each choice has; unloaded rules are all defaults. */
export function choiceRulesFrom(served: MetadataChoiceRules | undefined): ChoiceRules {
  const find = (key: string): ChoiceRule => {
    const choice = served?.choices.find((c) => c.choice === key);
    return choice
      ? {
          whenSourceNamesMain: choice.whenSourceNamesMain,
          whenSourceSilent: choice.whenSourceSilent,
        }
      : DEFAULT_CHOICE_RULE;
  };
  return { mainAuthor: find(MAIN_AUTHOR_CHOICE), primarySeries: find(PRIMARY_SERIES_CHOICE) };
}

/**
 * The pick and whether a person is wanted: `value` is the author (undefined = none) or, for a series,
 * the explicit primary (undefined = let the default decide); `needsAsk` says the rule is Ask me with a
 * real decision. With `needsAsk`, `value` is what to pre-select.
 */
export interface ChoiceOutcome {
  value: string | undefined;
  needsAsk: boolean;
}

/** Mirrors `NameNormalizer.Normalize`: case, dots and initials spacing are folded away. */
export function normalizeName(value: string | null | undefined): string {
  if (!value || !value.trim()) return "";
  const tokens = value
    .trim()
    .toLowerCase()
    .replaceAll("&", " and ")
    .replaceAll(".", " ")
    .replace(/\s+/g, " ")
    .trim()
    .split(" ")
    .filter(Boolean);

  const merged: string[] = [];
  let initials = "";
  for (const token of tokens) {
    if (token.length === 1) {
      initials += token;
    } else {
      if (initials) {
        merged.push(initials);
        initials = "";
      }
      merged.push(token);
    }
  }
  if (initials) merged.push(initials);
  return merged.join(" ");
}

/** Whether two names are the same author or series once case, dots and initials spacing are folded. */
export function sameName(a: string | null | undefined, b: string | null | undefined): boolean {
  const left = normalizeName(a);
  return left !== "" && left === normalizeName(b);
}

function distinct(names: readonly (string | null | undefined)[]): string[] {
  const result: string[] = [];
  for (const name of names) {
    const trimmed = name?.trim();
    if (trimmed && !result.some((r) => sameName(r, trimmed))) result.push(trimmed);
  }
  return result;
}

function find(candidates: readonly string[], name: string | null | undefined): string | undefined {
  return name?.trim() ? candidates.find((c) => sameName(c, name)) : undefined;
}

/**
 * The main author a book would have after taking `sourceAuthors` - see the server's
 * `ResolveMainAuthor`. `explicitChoice` is a person's pick ("" = no main author, undefined = no pick;
 * a name the source does not list is ignored). With fewer than two source authors there is nothing to
 * choose: the current main author is kept if the source still credits them, otherwise there is none.
 */
export function resolveMainAuthor(
  rule: ChoiceRule,
  sourceAuthors: readonly (string | null | undefined)[],
  sourceMain: string | null | undefined,
  currentMain: string | null | undefined,
  explicitChoice?: string,
): ChoiceOutcome {
  const candidates = distinct(sourceAuthors);
  const keepCurrent = find(candidates, currentMain);

  if (explicitChoice !== undefined) {
    if (explicitChoice === "") return { value: undefined, needsAsk: false };
    const picked = find(candidates, explicitChoice);
    if (picked !== undefined) return { value: picked, needsAsk: false };
  }

  if (candidates.length < 2) return { value: keepCurrent, needsAsk: false };

  const named = find(candidates, sourceMain);
  if (named !== undefined) {
    const byRule: Record<SourceNamesMainRule, ChoiceOutcome> = {
      UseSources: { value: named, needsAsk: false },
      AskMe: { value: keepCurrent, needsAsk: !sameName(named, keepCurrent) },
      KeepCurrent: { value: keepCurrent, needsAsk: false },
    };
    return byRule[rule.whenSourceNamesMain];
  }

  const bySilentRule: Record<SourceSilentRule, ChoiceOutcome> = {
    FirstListed: { value: keepCurrent ?? candidates[0], needsAsk: false },
    None: { value: undefined, needsAsk: false },
    AskMe: { value: keepCurrent, needsAsk: keepCurrent === undefined },
    KeepCurrent: { value: keepCurrent, needsAsk: false },
  };
  return bySilentRule[rule.whenSourceSilent];
}

/**
 * The primary series a book would take from `sourceSeries` (names in the order the source listed
 * them) - see the server's `ResolvePrimarySeries`. `value` is the explicit primary to hand
 * `chooseSeriesPrimary`, or undefined to let its default decide (keep the book's own when listed).
 */
export function resolvePrimarySeries(
  rule: ChoiceRule,
  sourceSeries: readonly (string | null | undefined)[],
  sourceMain: string | null | undefined,
  currentSeries: readonly string[],
  explicitChoice?: string,
): ChoiceOutcome {
  const candidates = distinct(sourceSeries);

  const picked = find(candidates, explicitChoice);
  if (picked !== undefined) return { value: picked, needsAsk: false };

  if (candidates.length < 2) return { value: undefined, needsAsk: false };

  const currentPrimary = currentSeries[0];
  const named = find(candidates, sourceMain);
  if (named !== undefined) {
    const byRule: Record<SourceNamesMainRule, ChoiceOutcome> = {
      UseSources: { value: named, needsAsk: false },
      AskMe: { value: undefined, needsAsk: !sameName(named, currentPrimary) },
      KeepCurrent: { value: undefined, needsAsk: false },
    };
    return byRule[rule.whenSourceNamesMain];
  }

  const survives = currentSeries.some((s) => find(candidates, s) !== undefined);
  const bySilentRule: Record<SourceSilentRule, ChoiceOutcome> = {
    FirstListed: { value: survives ? undefined : candidates[0], needsAsk: false },
    AskMe: { value: undefined, needsAsk: !survives },
    KeepCurrent: { value: undefined, needsAsk: false },
    // A book always has a primary series: the server refuses this rule for it, so it behaves as Keep current.
    None: { value: undefined, needsAsk: false },
  };
  return bySilentRule[rule.whenSourceSilent];
}

// --- Resolving both choices for one review -------------------------------------------------------

/** Authors in an input are joined as "A / B" (the edit form) or "A, B" (the list panels). */
const PERSON_SEPARATOR = /\s*\/\s*|,\s*/;

function namesOf(joined: string | undefined): string[] {
  return (joined ?? "")
    .split(PERSON_SEPARATOR)
    .map((n) => n.trim())
    .filter(Boolean);
}

export interface MetadataChoices {
  /** The source's authors, distinct, in its order: what the main author is picked from. */
  authorOptions: string[];
  /** The book's own main author while it is still one of its authors; "" = none. */
  currentMainAuthor: string;
  /** The main author the apply would give the book; "" = none. This is what the picker shows. */
  mainAuthor: string;
  mainAuthorNeedsAsk: boolean;
  /** The explicit primary to hand `chooseSeriesPrimary`; undefined = its own default decides. */
  primarySeries: string | undefined;
  primarySeriesNeedsAsk: boolean;
}

/**
 * Both choices for a book and a source result, after the user's explicit picks (`chosenMainAuthor`:
 * "" = no main author; `chosenPrimarySeries`). The one place the review dialogs and the pending-row
 * panel ask "what would this apply do", so the preselected radio, the diff row and the apply agree.
 */
export function resolveMetadataChoices(
  rules: ChoiceRules,
  current: Pick<OrganizeAudiobookInput, "authors" | "mainAuthor" | "series" | "additionalSeries">,
  result: Pick<MetadataSearchResult, "authors" | "mainAuthor" | "series" | "mainSeries">,
  chosenMainAuthor?: string,
  chosenPrimarySeries?: string,
): MetadataChoices {
  const authorOptions = distinct(result.authors.map((a) => a.name));
  const currentNames = namesOf(current.authors);
  const currentMain = currentNames.includes(current.mainAuthor?.trim() ?? "")
    ? current.mainAuthor?.trim()
    : undefined;
  const author = resolveMainAuthor(
    rules.mainAuthor,
    authorOptions,
    result.mainAuthor,
    currentMain,
    chosenMainAuthor,
  );

  const currentSeries = [
    ...(current.series?.trim() ? [current.series.trim()] : []),
    ...(current.additionalSeries ?? []).map((s) => s.seriesName),
  ];
  const series = resolvePrimarySeries(
    rules.primarySeries,
    (result.series ?? []).map((s) => s.seriesName),
    result.mainSeries,
    currentSeries,
    chosenPrimarySeries,
  );

  return {
    authorOptions,
    currentMainAuthor: currentMain ?? "",
    mainAuthor: author.value ?? "",
    mainAuthorNeedsAsk: author.needsAsk,
    primarySeries: series.value,
    primarySeriesNeedsAsk: series.needsAsk,
  };
}
