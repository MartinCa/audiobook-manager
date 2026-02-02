export default interface AudiobookDetail {
  id: number;
  bookName: string;
  subtitle?: string;
  series?: string;
  seriesPart?: string;
  year: number;
  authors: string[];
  narrators: string[];
  genres: string[];
  description?: string;
  copyright?: string;
  publisher?: string;
  rating?: string;
  asin?: string;
  www?: string;
  coverFilePath?: string;
  durationInSeconds?: number;
  filePath: string;
  fileName: string;
  sizeInBytes: number;
}
