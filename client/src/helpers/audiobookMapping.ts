import type { Audiobook } from "@/types/Audiobook";
import type { AudiobookDetail } from "@/types/AudiobookDetail";
import type { DiscoveredAudiobook } from "@/types/DiscoveredAudiobook";

function splitList(str?: string | null): string[] {
  if (!str) return [];
  return str
    .split("/")
    .map((s) => s.trim())
    .filter(Boolean);
}

/**
 * Normalizes either a DiscoveredAudiobook DTO or an AudiobookDetail DTO
 * into the frontend Audiobook model for use in BookEditForm.
 */
export function toAudiobook(dto: DiscoveredAudiobook | AudiobookDetail): Audiobook {
  if ("fullPath" in dto) {
    return {
      bookName: dto.bookName?.trim() ? dto.bookName : undefined,
      subtitle: dto.subtitle ?? undefined,
      series: dto.series ?? undefined,
      seriesPart: dto.seriesPart ?? undefined,
      year: dto.year ?? undefined,
      authors: splitList(dto.authors).map((name) => ({ name })),
      narrators: splitList(dto.narrators).map((name) => ({ name })),
      genres: splitList(dto.genres),
      description: dto.description ?? undefined,
      copyright: dto.copyright ?? undefined,
      publisher: dto.publisher ?? undefined,
      language: dto.language ?? undefined,
      rating: dto.rating ?? undefined,
      asin: dto.asin ?? undefined,
      www: dto.www ?? undefined,
      durationInSeconds: dto.durationInSeconds ?? undefined,
      fileInfo: {
        fullPath: dto.fullPath,
        fileName: dto.fileName,
        sizeInBytes: dto.sizeInBytes,
      },
    };
  }

  return {
    bookName: dto.bookName ?? undefined,
    subtitle: dto.subtitle ?? undefined,
    series: dto.series ?? undefined,
    seriesPart: dto.seriesPart ?? undefined,
    year: dto.year ?? undefined,
    authors: dto.authors.map((name) => ({ name })),
    narrators: dto.narrators.map((name) => ({ name })),
    genres: dto.genres,
    description: dto.description ?? undefined,
    copyright: dto.copyright ?? undefined,
    publisher: dto.publisher ?? undefined,
    language: dto.language ?? undefined,
    rating: dto.rating ?? undefined,
    asin: dto.asin ?? undefined,
    www: dto.www ?? undefined,
    durationInSeconds: dto.durationInSeconds ?? undefined,
    fileInfo: {
      fullPath: dto.filePath,
      fileName: dto.fileName,
      sizeInBytes: dto.sizeInBytes,
    },
  };
}
