export default interface ManagedAudiobook {
  id: number;
  bookName: string;
  subtitle?: string;
  series?: string;
  seriesPart?: string;
  year: number;
  authors: string[];
  narrators: string[];
  genres: string[];
  coverFilePath?: string;
  durationInSeconds?: number;
}
