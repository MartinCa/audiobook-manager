export default interface OrganizeAudiobookInput {
  cover_base64?: string;
  cover_mime?: string;
  authors?: string;
  narrators?: string;
  bookName?: string;
  subtitle?: string;
  series?: string;
  seriesOriginal?: string;
  seriesPart?: string;
  seriesPartWarning?: boolean;
  year?: number;
  genres?: string;
  description?: string;
  copyright?: string;
  publisher?: string;
  www?: string;
  rating?: number;
  asin?: string;
}
