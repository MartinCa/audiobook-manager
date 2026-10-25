/**
 * Returns `names` in the order the backend serves the sources in (`GET metadata-search/services`,
 * which already follows the order chosen in Library Settings). A name the list does not know keeps
 * its place after the known ones, so nothing is dropped.
 */
export function orderLikeServices(names: string[], services: { name: string }[]): string[] {
  const rank = new Map(services.map((s, i) => [s.name, i] as const));
  return names
    .map((name, position) => ({ name, position }))
    .sort(
      (a, b) =>
        (rank.get(a.name) ?? Number.MAX_SAFE_INTEGER) -
          (rank.get(b.name) ?? Number.MAX_SAFE_INTEGER) || a.position - b.position,
    )
    .map((x) => x.name);
}

/** A copy of `list` with the item at `index` moved by `delta` places (clamped to the list). */
export function moveItem<T>(list: T[], index: number, delta: number): T[] {
  const target = index + delta;
  if (index < 0 || index >= list.length || target < 0 || target >= list.length) return list;
  const next = [...list];
  next.splice(target, 0, ...next.splice(index, 1));
  return next;
}

/**
 * `services` in the given saved order, mirroring the server's rule: listed names first, in that
 * order, then the rest alphabetically.
 */
export function sortServicesByOrder<T extends { name: string }>(
  services: T[],
  order: string[],
): T[] {
  const rank = new Map(order.map((name, i) => [name.toLowerCase(), i] as const));
  const rankOf = (name: string) => rank.get(name.toLowerCase()) ?? Number.MAX_SAFE_INTEGER;
  return [...services].sort(
    (a, b) => rankOf(a.name) - rankOf(b.name) || (a.name < b.name ? -1 : a.name > b.name ? 1 : 0),
  );
}
