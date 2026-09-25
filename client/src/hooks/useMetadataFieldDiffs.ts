import { useMemo } from "react";
import { joinPersons } from "@/helpers/bookDetailsHelpers";
import { languageLabel, normalizeLanguage } from "@/helpers/languages";
import { splitList } from "@/helpers/organizeAudiobookInput";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";
import type { LanguageOption } from "@/types/Language";

/**
 * A plain code-point, case-insensitive comparison - what backend `StringComparer.OrdinalIgnoreCase`
 * means. Deliberately NOT `localeCompare`, even with a `sensitivity` option: locale-aware
 * comparison also folds accents (e.g. treats "Cafe" and "Café" as equal for sorting), which
 * `OrdinalIgnoreCase` does not, so a set mixing both forms could sort to a different joined
 * string on the two layers even though the visible order looks the same.
 */
function ordinalCompare(a: string, b: string): number {
  const al = a.toLowerCase();
  const bl = b.toLowerCase();
  return al < bl ? -1 : al > bl ? 1 : 0;
}

/**
 * Order/casing/duplication is presentation, not data: the same set of genre names in a
 * different order is not a change worth flagging - mirrors the backend's
 * MetadataRefreshDiffer.JoinList (trim, dedupe case-sensitive, sort ordinal-ignore-case) so this
 * comparison agrees with what actually gets stored as the pending row's changed-fields list.
 */
function comparableGenres(
  genres: readonly (string | null | undefined)[] | null | undefined,
): string {
  const meaningful = new Set(
    (genres ?? []).map((g) => g?.trim()).filter((g): g is string => Boolean(g)),
  );
  return Array.from(meaningful).sort(ordinalCompare).join("/");
}

// Matches both separators this hook's two call sites join `currentInput.authors`/`narrators`
// with: TagPreviewDialog's `currentInput` (BookEditForm.tsx) uses organizeAudiobookInput's
// `joinList` (" / "), while PendingRefreshRowPanel's uses `joinPersons` (", "). Splitting on a
// single hardcoded separator broke the other call site entirely (every comparison there was a
// one-token string vs the ", "-joined fetched value, so Authors/Narrators looked changed even
// when the sets were identical) - this accepts either shape so both callers get a real
// order/dedupe-insensitive comparison rather than just one of them.
const PERSON_LIST_SEPARATOR = /\s*\/\s*|,\s*/;

// A run of one or more single-letter-plus-dot tokens, fused ("M.R.") or spaced ("M. R."), sitting
// between word boundaries - "Andrew R. Chow" vs "Andrew R Chow" is the same author under a
// different punctuation convention, and "Stephen M. R. Covey" vs "Stephen M.R. Covey" the same
// under a different spacing one, neither a content change. An optional trailing space is only
// consumed when it is followed by ANOTHER letter-dot pair (the lookahead), so the space that
// actually separates the initials from the surname is left alone rather than fused into it - the
// bug an earlier, two-step version of this fold had (collapsing the space between "R." and
// "Chow" itself when it only meant to collapse the space between two adjacent initials).
// Comparison-only: the displayed values keep whatever punctuation the source and the library
// actually used.
const INITIALS_RUN = /(^|[\s/])((?:[A-Za-z]\.(?:\s+(?=[A-Za-z]\.))?)+)(?=[\s/]|$)/g;

function collapseInitials(name: string): string {
  return name.replace(INITIALS_RUN, (_match, boundary: string, run: string) => {
    const letters = run.match(/[A-Za-z]/g) ?? [];
    return boundary + letters.join(" ");
  });
}

/**
 * Mirrors the backend's MetadataRefreshDiffer.JoinNames (trim, dedupe case-sensitive, sort
 * ordinal-ignore-case) applied to an already-joined display string, plus the initials-run fold
 * above.
 */
function comparablePersonNames(joined: string): string {
  const meaningful = new Set(
    joined
      .split(PERSON_LIST_SEPARATOR)
      .map((name) => collapseInitials(name.trim()))
      .filter((name) => name.length > 0),
  );
  return Array.from(meaningful).sort(ordinalCompare).join(", ");
}

export interface FieldDiff {
  key: string;
  label: string;
  currentValue: string;
  newValue: string;
  changed: boolean;
}

const truncate = (str: string, length: number): string => {
  if (str.length <= length) return str;
  return str.substring(0, length) + "...";
};

/**
 * The field-by-field diff between a book's current values and a scraped/pending search result -
 * shared by TagPreviewDialog (the interactive "search online metadata" review) and
 * PendingRefreshRowPanel (the metadata-refresh list's per-row quick apply), so the two stay in
 * sync on exactly what counts as a change and how each field is labeled/formatted.
 */
export function useMetadataFieldDiffs(
  currentInput: OrganizeAudiobookInput,
  searchResult: MetadataSearchResult,
  languages: LanguageOption[],
): FieldDiff[] {
  return useMemo((): FieldDiff[] => {
    const cur = currentInput;
    const res = searchResult;

    const newAuthors = joinPersons(res.authors) ?? "";
    const newNarrators = joinPersons(res.narrators) ?? "";
    const firstSeries = res.series?.[0];
    const newSeries = firstSeries?.seriesName ?? "";
    const newSeriesPart = firstSeries?.seriesPart ?? "";
    const newGenres = res.genres?.join("/") ?? "";

    const currentLanguage = normalizeLanguage(cur.language, languages) ?? cur.language ?? "";
    const newLanguage = normalizeLanguage(res.language, languages) ?? currentLanguage;

    return [
      {
        key: "authors",
        label: "Authors",
        currentValue: cur.authors ?? "",
        newValue: newAuthors,
        changed: comparablePersonNames(cur.authors ?? "") !== comparablePersonNames(newAuthors),
      },
      {
        key: "narrators",
        label: "Narrators",
        currentValue: cur.narrators ?? "",
        newValue: newNarrators,
        changed: comparablePersonNames(cur.narrators ?? "") !== comparablePersonNames(newNarrators),
      },
      {
        key: "bookName",
        label: "Book Name",
        currentValue: cur.bookName ?? "",
        newValue: res.bookName ?? "",
        changed: (cur.bookName ?? "") !== (res.bookName ?? ""),
      },
      {
        key: "subtitle",
        label: "Subtitle",
        currentValue: cur.subtitle ?? "",
        newValue: res.subtitle ?? "",
        changed: (cur.subtitle ?? "") !== (res.subtitle ?? ""),
      },
      {
        key: "series",
        label: "Series",
        currentValue: [cur.series, cur.seriesPart].filter(Boolean).join(" #") || "",
        newValue: [newSeries, newSeriesPart].filter(Boolean).join(" #") || "",
        changed: (cur.series ?? "") !== newSeries || (cur.seriesPart ?? "") !== newSeriesPart,
      },
      {
        key: "year",
        label: "Year",
        currentValue: cur.year?.toString() ?? "",
        newValue: res.year?.toString() ?? "",
        // Year is deliberately never blanked (the backend differ excludes a null source year on
        // purpose, and the apply guards keep the current value when the source has none) - so a
        // missing source year must not be advertised as a change the apply will never make.
        changed: res.year != null && cur.year !== res.year,
      },
      {
        key: "genres",
        label: "Genres",
        currentValue: cur.genres ?? "",
        newValue: newGenres,
        changed: comparableGenres(splitList(cur.genres)) !== comparableGenres(res.genres),
      },
      {
        key: "description",
        label: "Description",
        currentValue: truncate(cur.description ?? "", 100),
        newValue: truncate(res.description ?? "", 100),
        changed: (cur.description ?? "") !== (res.description ?? ""),
      },
      {
        key: "rating",
        label: "Rating",
        currentValue: cur.rating?.toString() ?? "",
        newValue: res.rating?.toString() ?? "",
        changed: cur.rating?.toString() !== res.rating?.toString(),
      },
      {
        key: "publisher",
        label: "Publisher",
        currentValue: cur.publisher ?? "",
        newValue: res.publisher ?? "",
        changed: (cur.publisher ?? "") !== (res.publisher ?? ""),
      },
      {
        key: "language",
        label: "Language",
        currentValue: languageLabel(currentLanguage, languages),
        newValue: languageLabel(newLanguage, languages),
        changed: currentLanguage !== newLanguage,
      },
      {
        key: "copyright",
        label: "Copyright",
        currentValue: cur.copyright ?? "",
        newValue: res.copyright ?? "",
        changed: (cur.copyright ?? "") !== (res.copyright ?? ""),
      },
      {
        key: "asin",
        label: "ASIN",
        currentValue: cur.asin ?? "",
        newValue: res.asin ?? "",
        changed: (cur.asin ?? "") !== (res.asin ?? ""),
      },
      {
        key: "www",
        label: "URL",
        currentValue: cur.www ?? "",
        newValue: res.cleanUrl ?? "",
        changed: (cur.www ?? "") !== (res.cleanUrl ?? ""),
      },
      {
        key: "cover",
        label: "Cover",
        currentValue: cur.cover_base64 ? "Has cover" : "",
        newValue: res.imageUrl ?? "",
        changed: Boolean(res.imageUrl),
      },
    ];
  }, [currentInput, searchResult, languages]);
}
