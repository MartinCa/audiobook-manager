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

export function isNearMatch(
  input: string | null | undefined,
  candidate: string | null | undefined,
): boolean {
  const normInput = normalizeForMatch(input);
  const normCandidate = normalizeForMatch(candidate);
  if (!normInput || !normCandidate) return false;
  return normInput === normCandidate || normCandidate.includes(normInput);
}

export function narrowByQuery(candidates: string[], query: string, limit: number = 5): string[] {
  if (!query.trim()) return [];
  const normQuery = normalizeForMatch(query);
  return candidates.filter((c) => normalizeForMatch(c).includes(normQuery)).slice(0, limit);
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
