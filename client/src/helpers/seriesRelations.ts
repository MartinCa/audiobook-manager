import type {
  MetadataSearchResult,
  MetadataSeriesSearchResult,
} from "@/types/MetadataSearchResult";

/**
 * A book's series as a set with one primary - the client twin of the backend's
 * `SeriesRelationSet` (AudiobookManager.Services), kept in step with it so what the review
 * dialogs offer as a change is exactly what the server's differ would record and its applier would
 * write. Sources list a book's series in an order that carries no meaning, so nothing here depends
 * on it: entries are canonicalised and compared as sets. Name/part canonicalisation (trim, blank =
 * none, same series ignoring case) is the backend's `SeriesNormalization`.
 */
export interface SeriesEntry {
  name: string;
  part?: string;
}

export interface SeriesSet {
  primary?: SeriesEntry;
  additional: SeriesEntry[];
}

const NUMBER = /^\d+(?:\.\d+)?$/;
const RANGE = /^(\d+(?:\.\d+)?)\s*(?:-|–|—|to)\s*(\d+(?:\.\d+)?)$/i;

// Digit strings can be longer than any number type, so this works on the text.
function canonicalNumber(value: string): string {
  const [whole = "", fraction = ""] = value.split(".");
  const w = whole.replace(/^0+/, "") || "0";
  const f = fraction.replace(/0+$/, "");
  return f ? `${w}.${f}` : w;
}

function compareNumbers(a: string, b: string): number {
  const [aw = "", af = ""] = a.split(".");
  const [bw = "", bf = ""] = b.split(".");
  if (aw.length !== bw.length) return aw.length - bw.length;
  if (aw !== bw) return aw < bw ? -1 : 1;
  const len = Math.max(af.length, bf.length);
  const x = af.padEnd(len, "0");
  const y = bf.padEnd(len, "0");
  return x === y ? 0 : x < y ? -1 : 1;
}

/**
 * The canonical form of a series part - the twin of the backend's `SeriesNormalization.CleanPart`:
 * a plain number (`2`, `2.5`), a range of two numbers (`1-3`) or, for anything else, the trimmed
 * text. Numbers lose leading zeros and a trailing `.0`, a leading `Book`/`Vol`/`Volume` label is
 * dropped, and an en/em dash or `to` between two numbers becomes `-`. A range that runs backwards
 * is not one and is left as written. Undefined when blank.
 */
export function canonicalPart(part: string | null | undefined): string | undefined {
  const trimmed = part?.trim();
  if (!trimmed) return undefined;
  const body = trimmed.replace(/^(?:vol(?:ume)?\.?|book)\s*(?=\d)/i, "").trim();
  if (NUMBER.test(body)) return canonicalNumber(body);
  const range = RANGE.exec(body);
  if (range) {
    const start = canonicalNumber(range[1] ?? "");
    const end = canonicalNumber(range[2] ?? "");
    if (compareNumbers(start, end) <= 0) return `${start}-${end}`;
  }
  return trimmed;
}

/** A plain code-point, case-insensitive comparison - what backend `OrdinalIgnoreCase` means. */
function ordinalCompare(a: string, b: string): number {
  const al = a.toLowerCase();
  const bl = b.toLowerCase();
  if (al !== bl) return al < bl ? -1 : 1;
  return a < b ? -1 : a > b ? 1 : 0;
}

/**
 * Trims names and parts, drops blank names and merges the same name listed twice
 * (case-insensitively), keeping the first non-empty part. Ordered by name.
 */
export function canonicalizeSeries(entries: readonly SeriesEntry[] | undefined): SeriesEntry[] {
  const merged = new Map<string, SeriesEntry>();
  for (const entry of entries ?? []) {
    const name = entry.name?.trim();
    if (!name) continue;
    const part = canonicalPart(entry.part);
    const key = name.toLowerCase();
    const existing = merged.get(key);
    if (!existing) merged.set(key, { name, part });
    else if (!existing.part && part) existing.part = part;
  }
  return Array.from(merged.values()).sort((a, b) => ordinalCompare(a.name, b.name));
}

/**
 * Picks the primary from a source's series: the explicit choice if the source has it; else the
 * book's current primary if the source still lists it; else the first of the book's other series
 * the source lists; else the source's first. Keeping the current primary whenever it survives
 * means a refresh never re-files a book just because a source reordered its series.
 */
export function chooseSeriesPrimary(
  current: readonly SeriesEntry[],
  source: readonly SeriesEntry[],
  explicitPrimary?: string,
): SeriesEntry | undefined {
  if (source.length === 0) return undefined;
  const explicit = explicitPrimary?.trim();
  if (explicit) {
    const found =
      source.find((s) => s.name === explicit) ??
      source.find((s) => s.name.toLowerCase() === explicit.toLowerCase());
    if (found) return found;
  }
  for (const have of current) {
    const found = source.find((s) => s.name === have.name);
    if (found) return found;
  }
  return source[0];
}

/** The set a book would have after taking `source`, with the primary chosen. */
export function resolveSeriesSet(
  current: readonly SeriesEntry[],
  source: readonly SeriesEntry[] | undefined,
  explicitPrimary?: string,
): SeriesSet {
  const canonical = canonicalizeSeries(source);
  const primary = chooseSeriesPrimary(current, canonical, explicitPrimary);
  return { primary, additional: canonical.filter((e) => e !== primary) };
}

/** The set a book has now: its primary (when it has one) and its additional series. */
export function currentSeriesSet(
  series: string | undefined,
  seriesPart: string | undefined,
  additional: readonly { seriesName: string; seriesPart?: string | null }[] | undefined,
): SeriesSet {
  const primaryName = series?.trim();
  return {
    primary: primaryName ? { name: primaryName, part: canonicalPart(seriesPart) } : undefined,
    additional: (additional ?? [])
      .filter((r) => r.seriesName?.trim())
      .map((r) => ({ name: r.seriesName.trim(), part: canonicalPart(r.seriesPart) })),
  };
}

export function allSeries(set: SeriesSet): SeriesEntry[] {
  return set.primary ? [set.primary, ...set.additional] : [...set.additional];
}

function partsEquivalent(a: string | undefined, b: string | undefined): boolean {
  const left = canonicalPart(a);
  const right = canonicalPart(b);
  if (!left || !right) return !left && !right;
  return left.toLowerCase() === right.toLowerCase();
}

/**
 * Whether the sets differ in a way a user cares about: a series added or removed, a different part
 * in a series both have, or a different primary. Names compare exactly (a casing correction is a
 * real change); parts compare by equivalence, so "2" and "2.0" do not differ.
 */
export function seriesSetsDiffer(current: SeriesSet, proposed: SeriesSet): boolean {
  const currentAll = allSeries(current);
  const proposedAll = allSeries(proposed);
  if (currentAll.length !== proposedAll.length) return true;
  if ((current.primary?.name ?? "") !== (proposed.primary?.name ?? "")) return true;
  return currentAll.some((have) => {
    const match = proposedAll.find((p) => p.name === have.name);
    return !match || !partsEquivalent(have.part, match.part);
  });
}

/** The set as one display string, primary marked when there is more than one series. */
export function formatSeriesSet(set: SeriesSet): string {
  const all = allSeries(set);
  return all
    .map((e) => {
      const text = e.part ? `${e.name} #${e.part}` : e.name;
      return all.length > 1 && e === set.primary ? `${text} (primary)` : text;
    })
    .join("; ");
}

/** The entries of a scraped result's `series` list. */
export function sourceSeriesEntries(
  series: readonly MetadataSeriesSearchResult[] | undefined,
): SeriesEntry[] {
  return (series ?? []).map((s) => ({ name: s.seriesName, part: s.seriesPart ?? undefined }));
}

/**
 * `result` with its series reordered so the primary `chosenPrimary` (or, by default, the one a
 * refresh would choose for a book currently in `current`) comes first and the rest follow in
 * canonical order. Consumers apply "first = primary, the rest = additional". Nothing is added or
 * dropped beyond canonicalisation (blank names, repeated names).
 */
export function withPrimarySeriesFirst(
  result: MetadataSearchResult,
  current: readonly SeriesEntry[],
  chosenPrimary?: string,
): MetadataSearchResult {
  const resolved = resolveSeriesSet(current, sourceSeriesEntries(result.series), chosenPrimary);
  const ordered = allSeries(resolved);
  const series = ordered.map((entry) => {
    const original = result.series.find(
      (s) => s.seriesName.trim().toLowerCase() === entry.name.toLowerCase(),
    );
    return { ...original, seriesName: entry.name, seriesPart: entry.part };
  });
  return { ...result, series };
}
