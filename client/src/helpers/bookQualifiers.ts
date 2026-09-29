import type { BookQualifierOption } from "@/types/BookQualifier";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

/**
 * Client twin of `AudiobookManager.Domain.BookQualifiers`. It holds no qualifier list of its own:
 * every function takes the options served by `GET /api/settings/book-qualifiers`, so adding a
 * qualifier on the backend is the only change a new one needs.
 *
 * A book stores its clean name and series plus a set of qualifier keys; the suffixes only exist on
 * disk. Nothing here writes to a book - `applyQualifiers` builds the "Saved as" preview, and
 * `splitQualifiers` powers the *suggestion* to move a suffix a name already carries into the
 * qualifier set (the server never guesses that on its own).
 */

function optionFor(key: string, options: BookQualifierOption[]): BookQualifierOption | undefined {
  const lowered = key.trim().toLowerCase();
  return options.find((o) => o.key.toLowerCase() === lowered);
}

/** A qualifier's display label; an unknown key (one the registry no longer has) shows as itself. */
export function qualifierLabel(key: string, options: BookQualifierOption[]): string {
  return optionFor(key, options)?.label ?? key;
}

/**
 * Canonical form of a set of keys, matching the server: trimmed, lowercased, de-duplicated and in
 * alphabetical label order. Unknown keys are kept and sort after the known ones.
 */
export function normalizeQualifiers(
  keys: readonly string[] | null | undefined,
  options: BookQualifierOption[],
): string[] {
  const seen = new Set<string>();
  const known: BookQualifierOption[] = [];
  const unknown: string[] = [];

  for (const raw of keys ?? []) {
    const key = raw?.trim().toLowerCase();
    if (!key || seen.has(key)) continue;
    seen.add(key);
    const option = optionFor(key, options);
    if (option) known.push(option);
    else unknown.push(key);
  }

  known.sort((a, b) => compareOrdinalIgnoreCase(a.label, b.label));
  unknown.sort();
  return [...known.map((o) => o.key), ...unknown];
}

function compareOrdinalIgnoreCase(a: string, b: string): number {
  const left = a.toLowerCase();
  const right = b.toLowerCase();
  if (left < right) return -1;
  return left > right ? 1 : 0;
}

/**
 * The name as it will be written to disk: the clean name followed by one " (Label)" per
 * qualifier, alphabetically. A blank name is returned unchanged.
 */
export function applyQualifiers(
  name: string | null | undefined,
  keys: readonly string[] | null | undefined,
  options: BookQualifierOption[],
): string {
  if (!name || !name.trim()) return name ?? "";

  const suffix = normalizeQualifiers(keys, options)
    .map((key) => optionFor(key, options)?.suffix ?? "")
    .join("");
  return name + suffix;
}

/**
 * Peels every known qualifier suffix off the end of `value`, in whatever order they appear.
 * Never strips the whole value away.
 */
function stripKnownSuffixes(
  value: string,
  options: BookQualifierOption[],
): { value: string; keys: string[] } {
  const keys: string[] = [];
  let current = value;

  for (;;) {
    const match = options.find((o) => {
      const suffix = o.suffix.toLowerCase();
      const lowered = current.toLowerCase();
      return (
        !keys.includes(o.key) &&
        lowered.length > suffix.length &&
        lowered.endsWith(suffix) &&
        current.slice(0, current.length - suffix.length).trim() !== ""
      );
    });
    if (!match) break;
    current = current.slice(0, current.length - match.suffix.length);
    keys.push(match.key);
  }

  return { value: current, keys };
}

export interface QualifierSplit {
  bookName: string;
  series?: string;
  /** Canonical keys of the suffixes that were found; empty when the name carries none. */
  qualifiers: string[];
}

/**
 * Suggests moving qualifier suffixes a name already carries into the qualifier set: "Killing Floor
 * (Dramatized)" becomes "Killing Floor" + dramatized. The series is only cleaned when it carries
 * the same suffixes as the name - a series that does not is left as it is. Used for scraped titles
 * and for the edit form's "move to qualifiers" hint; never applied silently to a stored book.
 */
export function splitQualifiers(
  bookName: string | null | undefined,
  series: string | null | undefined,
  options: BookQualifierOption[],
): QualifierSplit {
  const name = bookName ?? "";
  const stripped = stripKnownSuffixes(name, options);
  if (stripped.keys.length === 0) {
    return { bookName: name, series: series ?? undefined, qualifiers: [] };
  }

  const qualifiers = normalizeQualifiers(stripped.keys, options);
  let cleanSeries = series ?? undefined;
  if (cleanSeries) {
    const strippedSeries = stripKnownSuffixes(cleanSeries, options);
    const sameSet =
      strippedSeries.keys.length === stripped.keys.length &&
      normalizeQualifiers(strippedSeries.keys, options).join(",") === qualifiers.join(",");
    if (sameSet) cleanSeries = strippedSeries.value;
  }

  return { bookName: stripped.value, series: cleanSeries, qualifiers };
}

/**
 * Moves qualifier suffixes a scraped title carries ("Killing Floor (Dramatized)") off the title
 * and series of a metadata search result, returning the cleaned result and the qualifier keys it
 * found. The same result object is returned when there is nothing to move, so callers that key
 * state on its identity (TagPreviewDialog resets its selection whenever the result changes) are
 * not disturbed. The caller decides what to do with the keys - the edit form pre-selects them.
 */
export function cleanSearchResult(
  result: MetadataSearchResult,
  options: BookQualifierOption[],
): { result: MetadataSearchResult; qualifiers: string[] } {
  const split = splitQualifiers(result.bookName, result.series?.[0]?.seriesName, options);
  if (split.qualifiers.length === 0) {
    return { result, qualifiers: [] };
  }

  const series = result.series?.map((s, index) =>
    index === 0 && split.series !== undefined ? { ...s, seriesName: split.series } : s,
  );
  return { result: { ...result, bookName: split.bookName, series }, qualifiers: split.qualifiers };
}
