export function foldAccents(str: string | null | undefined): string {
  if (!str) return "";
  return str.normalize("NFD").replace(/[\u0300-\u036f]/g, "");
}

export function normalizeForMatch(value: string | null | undefined): string {
  if (!value) return "";
  return foldAccents(value)
    .toLowerCase()
    .replace(/[^\w\s]/g, "")
    .replace(/\s+/g, " ")
    .trim();
}

// "J. K. Rowling" and "J.K. Rowling" name the same person; the space after a dotted initial is
// a typographical variant, not a different token. Collapse it BEFORE normalizeForMatch strips
// the dots, so the two forms compare equal when matching. Deliberately surgical: only a space
// between a dotted initial and the next letter is collapsed, so "Harry Pot ter" still does NOT
// match "Harry Potter".
function foldInitialSpacing(value: string): string {
  return value.replace(/([A-Za-z]\.)\s+(?=[A-Za-z])/g, "$1");
}

function normalizedFolded(value: string | null | undefined): string {
  if (!value) return "";
  return normalizeForMatch(foldInitialSpacing(value));
}

export function isNearMatch(
  input: string | null | undefined,
  candidate: string | null | undefined,
): boolean {
  const normInput = normalizeForMatch(input);
  const normCandidate = normalizeForMatch(candidate);
  if (!normInput || !normCandidate) return false;
  return (
    normInput === normCandidate ||
    normCandidate.includes(normInput) ||
    normalizedFolded(candidate).includes(normalizedFolded(input))
  );
}

export function narrowByQuery(candidates: string[], query: string, limit: number = 5): string[] {
  if (!query.trim()) return [];
  const normQuery = normalizeForMatch(query);
  const foldedQuery = normalizedFolded(query);
  return candidates
    .filter(
      (c) => normalizeForMatch(c).includes(normQuery) || normalizedFolded(c).includes(foldedQuery),
    )
    .slice(0, limit);
}

export function findSimilarExisting(
  input: string | null | undefined,
  candidates: string[],
): string[] {
  if (!input || !input.trim()) return [];
  const normInput = normalizeForMatch(input);
  if (!normInput) return [];
  return candidates.filter((c) => {
    const normCandidate = normalizeForMatch(c);
    return (
      normCandidate.includes(normInput) ||
      normInput.includes(normCandidate) ||
      normCandidate.replace(/\s/g, "") === normInput.replace(/\s/g, "")
    );
  });
}

// Applies a "similar existing value" hint's suggestion at a given index. A plain
// current.map((v, i) => i === index ? suggestion : v) can silently create a duplicate: the
// suggestion is, by construction, already very close to another entry, and is sometimes an
// exact match for one already sitting elsewhere in the same array (e.g. two authors "Brandon
// Sanderson" and "Brandon Sandersons" both present, with the hint on the typo suggesting the
// exact name of the other). The input control itself refuses to create that duplicate through
// typing or in-place editing, but a hint click bypasses it and calls the form's onChange
// directly, so it needs the same guard. When the suggestion already exists elsewhere, the
// flagged (typo'd) entry is dropped instead of duplicated - the canonical entry is already
// present.
export function applyHintSuggestion(
  current: string[],
  index: number,
  suggestion: string,
): string[] {
  const existsElsewhere = current.some(
    (v, i) => i !== index && v.toLowerCase() === suggestion.toLowerCase(),
  );
  if (existsElsewhere) {
    return current.filter((_, i) => i !== index);
  }
  return current.map((v, i) => (i === index ? suggestion : v));
}
