import BookFileInfo from "./BookFileInfo";

export interface AudiobookImage {
  base64Data: string;
  mimeType: string;
}

export interface Audiobook {
  authors: AudiobookPerson[];
  narrators: AudiobookPerson[];
  bookName?: string;
  subtitle?: string;
  series?: string;
  seriesPart?: string;
  year?: number;
  genres: string[];
  description?: string;
  copyright?: string;
  publisher?: string;
  rating?: string;
  asin?: string;
  www?: string;

  cover?: AudiobookImage;

  durationInSeconds?: number;

  fileInfo?: BookFileInfo;
}

export interface AudiobookPerson {
  name: string;
  role?: string;
}
