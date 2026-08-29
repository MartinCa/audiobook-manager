import { LanguageOption } from "../types/Language";

/**
 * Client-side helpers for the managed language list.
 *
 * Deliberately no literal list of codes lives here — every function takes the options fetched
 * from `GET /settings/languages`, so the backend's `Languages` class stays the only place the
 * supported set is written down.
 */

/**
 * Folds a free-text language value to one of the supported codes, mirroring the backend's
 * `Languages.Normalize`. Values reach the client from scrapes ("English") and from m4b tags
 * written elsewhere ("eng", "Dansk", "en-US"), and all of them have to land on the same option
 * as one picked from the select.
 *
 * Returns undefined when the value is empty or names a language that isn't in the list — the
 * caller decides whether that means "leave it alone" or "use the default".
 */
export function normalizeLanguage(
  raw: string | undefined | null,
  languages: LanguageOption[],
): string | undefined {
  if (!raw || !raw.trim()) {
    return undefined;
  }

  let value = raw.trim().toLowerCase();

  // Region-qualified tags ("en-US", "da_DK") name the same language as their base subtag.
  const separatorIndex = value.search(/[-_]/);
  if (separatorIndex > 0) {
    value = value.slice(0, separatorIndex);
  }

  const match = languages.find(
    (l) =>
      l.code.toLowerCase() === value ||
      l.displayName.toLowerCase() === value ||
      // The rest of the spellings ("eng", "dan", the endonym "Dansk") come from the backend's
      // own alias table rather than being guessed here, so the two folds cannot drift.
      (l.aliases ?? []).includes(value),
  );

  return match?.code;
}

/** The name to show for a stored code; an unmanaged value is shown as-is rather than hidden. */
export function languageLabel(
  code: string | undefined | null,
  languages: LanguageOption[],
): string {
  if (!code) {
    return "";
  }
  return languages.find((l) => l.code === code)?.displayName ?? code;
}

/**
 * The items for a language select, with the current value appended when it names something the
 * library doesn't manage. Without this a strict select renders empty for such a book and the
 * next save silently wipes a real value.
 */
export function languageSelectItems(
  current: string | undefined | null,
  languages: LanguageOption[],
): LanguageOption[] {
  if (!current || languages.some((l) => l.code === current)) {
    return languages;
  }
  return [
    ...languages,
    { code: current, displayName: `${current} (unrecognized)`, aliases: [] },
  ];
}
