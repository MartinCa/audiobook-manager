export interface Language {
  code: string;
  name: string;
}

export interface LanguageOption {
  code: string;
  name: string;
}

export function normalizeLanguage(
  input: string | null | undefined,
  languages: Language[]
): string {
  if (!input) return "";
  const normInput = foldAccents(input).toLowerCase().trim();
  const found = languages.find(
    (l) =>
      l.code.toLowerCase() === normInput ||
      foldAccents(l.name).toLowerCase() === normInput
  );
  return found ? found.code : input;
}

export function languageLabel(
  code: string | null | undefined,
  languages: Language[]
): string {
  if (!code) return "";
  const found = languages.find(
    (l) => l.code.toLowerCase() === code.toLowerCase()
  );
  return found ? found.name : code;
}

export function languageSelectItems(
  currentValue: string | null | undefined,
  languages: Language[]
): LanguageOption[] {
  const options = [...languages];
  if (
    currentValue &&
    !languages.some(
      (l) => l.code.toLowerCase() === currentValue.toLowerCase()
    )
  ) {
    options.push({ code: currentValue, name: `${currentValue} (unrecognized)` });
  }
  return options;
}

function foldAccents(str: string): string {
  return str.normalize("NFD").replace(/[\u0300-\u036f]/g, "");
}
