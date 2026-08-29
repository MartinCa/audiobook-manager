/**
 * Lightweight client-side near-duplicate matcher used for entry-time duplicate
 * prevention (autocomplete narrowing + "similar entries exist" hints) in the
 * add/edit book form. This is advisory UI only - it does not need to match the
 * backend SimilarityGrouper byte-for-byte and is never the source of truth for
 * the bulk-align feature.
 */

/**
 * Strips combining diacritics (e.g. "é" -> "e") so accent-insensitive comparisons can be done
 * with a plain equality/substring check. JS string comparison never folds accents on its own -
 * "rene".includes("rené") is false without this.
 */
export function foldAccents(value: string): string {
  return value.normalize("NFD").replace(/[\u0300-\u036f]/g, "");
}

export function normalizeForMatch(value: string | null | undefined): string {
  if (!value) return "";
  let normalized = foldAccents(value.trim().toLowerCase());
  normalized = normalized.replace(/&/g, " and ");
  normalized = normalized.replace(/\./g, " ");
  normalized = normalized.replace(/\s+/g, " ").trim();

  const tokens = normalized.split(" ").filter((t) => t.length > 0);
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

function levenshtein(a: string, b: string): number {
  if (!a) return b.length;
  if (!b) return a.length;

  const rows = a.length + 1;
  const cols = b.length + 1;
  const distances: number[][] = Array.from({ length: rows }, () =>
    new Array(cols).fill(0),
  );

  for (let i = 0; i < rows; i++) distances[i][0] = i;
  for (let j = 0; j < cols; j++) distances[0][j] = j;

  for (let i = 1; i < rows; i++) {
    for (let j = 1; j < cols; j++) {
      const cost = a[i - 1] === b[j - 1] ? 0 : 1;
      distances[i][j] = Math.min(
        distances[i - 1][j] + 1,
        distances[i][j - 1] + 1,
        distances[i - 1][j - 1] + cost,
      );
    }
  }

  return distances[rows - 1][cols - 1];
}

/**
 * Folded forms of a name list, cached against the array itself. Autocomplete narrowing runs on
 * every keystroke over the whole list, and folding is an NFD normalize plus a regex per value -
 * so it is done once per list rather than once per list per character typed. Keyed on array
 * identity, so it invalidates for free when SimilarValueService swaps in a refreshed list.
 *
 * Array identity alone is not enough, though: a caller may also grow the same array in place
 * (BookEditForm appends a newly-saved author to the list it hands us). The cached fold was then
 * shorter than the array it described, and narrowByQuery - which indexes it by the *array's*
 * length - read past its end and threw "Cannot read properties of undefined". Comparing lengths
 * catches every append/removal, which is the only in-place mutation this list ever sees.
 */
const foldedListCache = new WeakMap<readonly string[], string[]>();

function foldedList(values: string[]): string[] {
  let folded = foldedListCache.get(values);
  if (!folded || folded.length !== values.length) {
    folded = values.map((v) => foldAccents(v.toLowerCase()));
    foldedListCache.set(values, folded);
  }
  return folded;
}

function maxDistanceForLength(length: number): number {
  if (length <= 4) return 0;
  if (length <= 8) return 1;
  return 2;
}

/**
 * Whether two already-normalized values are near-duplicates (but not identical).
 */
function isNearMatchNormalized(
  normValue: string,
  normExisting: string,
): boolean {
  if (!normValue || !normExisting) return false;
  if (normValue === normExisting) return false; // exact match isn't a "similar" hint

  const threshold = maxDistanceForLength(
    Math.min(normValue.length, normExisting.length),
  );
  if (threshold <= 0) return false;

  // The length difference is a lower bound on the edit distance, so it rules a pair out
  // before the O(n*m) matrix is built. Mirrors the same short-circuit the backend's
  // SeriesService.NormalizedSimilarity applies.
  if (Math.abs(normValue.length - normExisting.length) > threshold)
    return false;

  return levenshtein(normValue, normExisting) <= threshold;
}

/**
 * Returns true if `value` is a near-duplicate (but not identical) to `existing`.
 */
export function isNearMatch(value: string, existing: string): boolean {
  return isNearMatchNormalized(
    normalizeForMatch(value),
    normalizeForMatch(existing),
  );
}

/**
 * Finds existing values that are a near-duplicate of `value` (excludes exact matches).
 */
export function findSimilarExisting(
  value: string,
  existingValues: string[],
): string[] {
  if (!value) return [];

  // Normalize the query once, not once per candidate.
  const normValue = normalizeForMatch(value);
  if (!normValue) return [];

  return existingValues.filter((existing) =>
    isNearMatchNormalized(normValue, normalizeForMatch(existing)),
  );
}

/**
 * Narrows `existingValues` to those that start with or contain `query`
 * (case-insensitive), for autocomplete-while-typing.
 */
export function narrowByQuery(
  query: string,
  existingValues: string[],
  limit = 10,
): string[] {
  const trimmed = foldAccents(query.trim().toLowerCase());
  if (!trimmed) return [];

  const folded = foldedList(existingValues);
  const matches: string[] = [];
  // Stops at `limit` instead of folding and filtering the entire list first.
  for (let i = 0; i < existingValues.length && matches.length < limit; i++) {
    if (folded[i].includes(trimmed)) {
      matches.push(existingValues[i]);
    }
  }
  return matches;
}
