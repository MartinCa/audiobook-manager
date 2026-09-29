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
 * Canonical form of a set of keys: trimmed, lowercased, de-duplicated and in alphabetical label
 * order, matching the server's `BookQualifiers.Normalize` for every key the list knows.
 *
 * Deliberate divergence: the server drops a key the registry does not know (it has no label, so it
 * can never be written into a name and read back), while this twin keeps it, after the known ones,
 * so a retired key on a stored book stays visible - `QualifiersField` shows it and the user can
 * remove it - until the server drops it on the next save. Do not "fix" one side to match the other.
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
 *
 * Deliberately more lenient than the server's read-back (`BookQualifiers.ApplyExpected`), which
 * only accepts the canonical alphabetical order: this only decides what gets cleaned *before* a
 * value is stored, and the server normalizes the stored set, so what is written to disk is always
 * canonical whatever order the scrape used.
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

  // Every series entry that carries exactly the title's suffixes is cleaned, not just the first:
  // whichever one the user applies must not leave a suffix behind that would be doubled on disk.
  // An entry with a different (or no) suffix set is left as it is.
  const series = result.series?.map((s) => {
    const stripped = stripKnownSuffixes(s.seriesName, options);
    const sameSet =
      stripped.keys.length > 0 &&
      normalizeQualifiers(stripped.keys, options).join(",") === split.qualifiers.join(",");
    return sameSet ? { ...s, seriesName: stripped.value } : s;
  });
  return { result: { ...result, bookName: split.bookName, series }, qualifiers: split.qualifiers };
}
