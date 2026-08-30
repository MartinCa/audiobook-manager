// Matches AudiobookManager.Api.Dtos.DiscoveredAudiobookDto exactly: a flat shape (no nested
// fileInfo) with authors/narrators/genres as "/"-joined strings, not arrays — same convention
// as the organize form fields (see helpers/organizeAudiobookInput.ts's splitList/joinList).
export interface DiscoveredAudiobook {
  fullPath: string;
  fileName: string;
  sizeInBytes: number;
  bookName?: string;
  subtitle?: string;
  series?: string;
  seriesPart?: string;
  year?: number;
  authors?: string;
  narrators?: string;
  genres?: string;
  isWellTagged: boolean;
  isDuplicate: boolean;
}

export type { DiscoveredAudiobook as default };
