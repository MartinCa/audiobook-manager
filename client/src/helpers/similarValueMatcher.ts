/**
 * Lightweight client-side near-duplicate matcher used for entry-time duplicate
 * prevention (autocomplete narrowing + "similar entries exist" hints) in the
 * add/edit book form. This is advisory UI only - it does not need to match the
 * backend SimilarityGrouper byte-for-byte and is never the source of truth for
 * the bulk-align feature.
 */

export function normalizeForMatch(value: string | null | undefined): string {
  if (!value) return "";
  let normalized = value.trim().toLowerCase();
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

function maxDistanceForLength(length: number): number {
  if (length <= 4) return 0;
  if (length <= 8) return 1;
  return 2;
}

/**
 * Returns true if `value` is a near-duplicate (but not identical) to `existing`.
 */
export function isNearMatch(value: string, existing: string): boolean {
  const normValue = normalizeForMatch(value);
  const normExisting = normalizeForMatch(existing);
  if (!normValue || !normExisting) return false;
  if (normValue === normExisting) return false; // exact match isn't a "similar" hint

  const threshold = maxDistanceForLength(
    Math.min(normValue.length, normExisting.length),
  );
  if (threshold <= 0) return false;

  return levenshtein(normValue, normExisting) <= threshold;
}

/**
 * Finds existing values that are a near-duplicate of `value` (excludes exact matches).
 */
export function findSimilarExisting(
  value: string,
  existingValues: string[],
): string[] {
  if (!value) return [];
  return existingValues.filter((existing) => isNearMatch(value, existing));
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
  const trimmed = query.trim().toLowerCase();
  if (!trimmed) return [];
  return existingValues
    .filter((v) => v.toLowerCase().includes(trimmed))
    .slice(0, limit);
}
