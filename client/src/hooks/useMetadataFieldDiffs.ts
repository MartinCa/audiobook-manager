import { useMemo } from "react";
import { joinPersons } from "@/helpers/bookDetailsHelpers";
import { languageLabel, normalizeLanguage } from "@/helpers/languages";
import { splitList } from "@/helpers/organizeAudiobookInput";
import { foldInitialSpacing } from "@/helpers/similarValueMatcher";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";
import type { LanguageOption } from "@/types/Language";

/**
 * Order/casing/duplication is presentation, not data: the same set of genre names in a
 * different order is not a change worth flagging - mirrors the backend's
 * MetadataRefreshDiffer.JoinList (trim, dedupe, sort ordinal-ignore-case) so this comparison
 * agrees with what actually gets stored as the pending row's changed-fields list.
 */
function comparableGenres(
  genres: readonly (string | null | undefined)[] | null | undefined,
): string {
  const meaningful = new Set(
    (genres ?? []).map((g) => g?.trim()).filter((g): g is string => Boolean(g)),
  );
  return Array.from(meaningful)
    .sort((a, b) => a.localeCompare(b, undefined, { sensitivity: "base" }))
    .join("/");
}

/**
 * "M. R." vs "M.R." is a typographical variant of the same name, not a content difference -
 * mirrors the backend's MetadataRefreshDiffer initials-spacing fold, and the fold this hook's
 * own foldInitialSpacing collapse already applies for type-ahead matching.
 */
function comparablePersonNames(joined: string): string {
  return foldInitialSpacing(joined);
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
