export function splitList(str: string | null | undefined): string[] {
  if (!str) return [];
  return str
    .split("/")
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

export function joinList(arr: string[] | null | undefined): string {
  if (!arr) return "";
  return arr.filter((s) => s && s.trim().length > 0).join(" / ");
}

export interface OrganizeAudiobookFormState {
  bookName?: string;
  subtitle?: string;
  authors?: string;
  narrators?: string;
  series?: string;
  seriesPart?: string;
  year?: number | string;
  genres?: string;
  description?: string;
  copyright?: string;
  publisher?: string;
  rating?: string;
  asin?: string;
  www?: string;
  language?: string;
  cover_base64?: string;
}

export function convertInputToAudiobook(
  input: OrganizeAudiobookFormState,
  fullPath: string
) {
  return {
    fullPath,
    bookName: input.bookName || "",
    subtitle: input.subtitle || undefined,
    authors: splitList(input.authors),
    narrators: splitList(input.narrators),
    series: input.series || undefined,
    seriesPart: input.seriesPart || undefined,
    year: input.year ? Number(input.year) : undefined,
    genres: splitList(input.genres),
    description: input.description || undefined,
    copyright: input.copyright || undefined,
    publisher: input.publisher || undefined,
    rating: input.rating || undefined,
    asin: input.asin || undefined,
    www: input.www || undefined,
    language: input.language || undefined,
    cover: input.cover_base64 || undefined,
  };
}
