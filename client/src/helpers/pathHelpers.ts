/**
 * Compares two filesystem paths for equality across Windows and POSIX separators,
 * matching the backend AudiobookFileHandler.PathsEqual semantics.
 */
export function pathsEqual(pathA?: string | null, pathB?: string | null): boolean {
  if (!pathA && !pathB) return true;
  if (!pathA || !pathB) return false;

  const normalize = (p: string) => p.trim().replace(/\\/g, "/").replace(/\/+$/, "").toLowerCase();

  return normalize(pathA) === normalize(pathB);
}
