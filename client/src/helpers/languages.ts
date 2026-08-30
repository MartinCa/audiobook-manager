import type { LanguageOption } from "@/types/Language";

export function normalizeLanguage(
  raw: string | undefined | null,
  languages: LanguageOption[],
): string | undefined {
  if (!raw || !raw.trim()) {
    return undefined;
  }

  let value = raw.trim().toLowerCase();

  const separatorIndex = value.search(/[-_]/);
  if (separatorIndex > 0) {
    value = value.slice(0, separatorIndex);
  }

  const match = languages.find(
    (l) =>
      l.code.toLowerCase() === value ||
      l.displayName.toLowerCase() === value ||
      (l.aliases ?? []).includes(value),
  );

  return match?.code;
}

export function languageLabel(
  code: string | undefined | null,
  languages: LanguageOption[],
): string {
  if (!code) {
    return "";
  }
  return languages.find((l) => l.code === code)?.displayName ?? code;
}

export function languageSelectItems(
  current: string | undefined | null,
  languages: LanguageOption[],
): LanguageOption[] {
  if (!current || languages.some((l) => l.code === current)) {
    return languages;
  }
  return [...languages, { code: current, displayName: `${current} (unrecognized)`, aliases: [] }];
}
