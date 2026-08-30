import type { AudiobookPerson } from "./Audiobook";
import type BookFileInfo from "./BookFileInfo";

export interface DiscoveredAudiobook {
  id?: number;
  fullPath: string;
  filename: string;
  bookName?: string;
  authors: AudiobookPerson[];
  narrators: AudiobookPerson[];
  series?: string;
  seriesPart?: string;
  year?: number;
  genres: string[];
  description?: string;
  copyright?: string;
  publisher?: string;
  language?: string;
  rating?: string;
  asin?: string;
  www?: string;
  isWellTagged: boolean;
  isDuplicate: boolean;
  fileInfo?: BookFileInfo;
}

export type { DiscoveredAudiobook as default };
