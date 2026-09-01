import type { LanguageOption } from "@/types/Language";

const DEFAULT_FALLBACK_ALIASES: Record<string, string> = {
  en: "en",
  eng: "en",
  english: "en",
  da: "da",
  dan: "da",
  danish: "da",
  dansk: "da",
};

export function normalizeLanguage(
  raw: string | undefined | null,
  languages: LanguageOption[] = [],
): string | undefined {
  if (!raw || !raw.trim()) {
    return undefined;
  }

  let value = raw.trim().toLowerCase();

  const separatorIndex = value.search(/[-_]/);
  if (separatorIndex > 0) {
    value = value.slice(0, separatorIndex);
  }

  if (languages.length > 0) {
    const match = languages.find(
      (l) =>
        l.code.toLowerCase() === value ||
        l.displayName.toLowerCase() === value ||
        (l.aliases ?? []).some((a) => a.toLowerCase() === value),
    );
    return match?.code;
  }

  return DEFAULT_FALLBACK_ALIASES[value];
}

const DEFAULT_FALLBACK_NAMES: Record<string, string> = {
  en: "English",
  da: "Danish",
};

export function languageLabel(
  code: string | undefined | null,
  languages: LanguageOption[],
): string {
  if (!code) {
    return "";
  }
  const normalized = normalizeLanguage(code, languages) ?? code;
  return (
    languages.find((l) => l.code === normalized)?.displayName ??
    DEFAULT_FALLBACK_NAMES[normalized.toLowerCase()] ??
    code
  );
}

export function languageSelectItems(
  current: string | undefined | null,
  languages: LanguageOption[],
): LanguageOption[] {
  if (!current) {
    return languages;
  }
  const normalized = normalizeLanguage(current, languages);
  if (normalized && languages.some((l) => l.code === normalized)) {
    return languages;
  }
  if (languages.some((l) => l.code === current)) {
    return languages;
  }
  return [...languages, { code: current, displayName: `${current} (unrecognized)`, aliases: [] }];
}
