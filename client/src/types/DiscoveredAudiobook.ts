export default interface DiscoveredAudiobook {
  fullPath: string;
  fileName: string;
  sizeInBytes: number;
  bookName: string;
  subtitle?: string;
  series?: string;
  seriesPart?: string;
  year?: number;
  authors?: string;
  narrators?: string;
  genres?: string;
  isWellTagged: boolean;
  isDuplicate: boolean;

  queueId?: string;
  queueProgress?: number;
  queueMessage?: string;
  error?: string;
}
