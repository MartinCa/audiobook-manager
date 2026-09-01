// Not generated: this is the tag-preview-diff shape BookEditForm/TagPreviewDialog build to show
// what will be written to the m4b — it's never sent to the backend as-is (the actual save goes
// through toAudiobookDto's OrganizeAudiobookDto/AudiobookDto shape), so there's no wire DTO to
// generate it from.
export interface OrganizeAudiobookInput {
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
  language?: string;
  www?: string;
  rating?: number;
  asin?: string;
}

export type { OrganizeAudiobookInput as default };
