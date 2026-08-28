import OrganizeAudiobookInput from "../types/OrganizeAudiobookInput";
import { Audiobook, AudiobookImage } from "../types/Audiobook";
import BookFileInfo from "../types/BookFileInfo";

// Shared by BookOrganize.vue and library/BookDetail.vue - both edit the same
// OrganizeAudiobookInput shape and need to turn it back into the Audiobook shape the
// generate-path/save endpoints expect. Only the duration/fileInfo, which come from whichever
// already-loaded book the form is editing, differ per caller.
// Splitting a blank field yields [""], not [] - which the backend would otherwise persist as a
// Person or Genre row with an empty name.
function splitList(value: string | undefined, separator: string): string[] {
  return (value ?? "")
    .split(separator)
    .map((part) => part.trim())
    .filter((part) => part.length > 0);
}

const splitNames = (value: string | undefined): string[] =>
  splitList(value, ",");

export function convertInputToAudiobook(
  input: OrganizeAudiobookInput,
  meta: { durationInSeconds?: number; fileInfo?: BookFileInfo },
): Audiobook {
  let cover: AudiobookImage | undefined = undefined;
  if (input.cover_base64 && input.cover_mime) {
    cover = {
      base64Data: input.cover_base64,
      mimeType: input.cover_mime,
    };
  }

  return {
    authors: splitNames(input.authors).map((name) => ({ name })),
    narrators: splitNames(input.narrators).map((name) => ({ name })),
    bookName: input.bookName,
    subtitle: input.subtitle,
    series: input.series,
    seriesPart: input.seriesPart,
    year: input.year,
    genres: splitList(input.genres, "/"),
    description: input.description,
    copyright: input.copyright,
    publisher: input.publisher,
    rating: input.rating?.toString(),
    asin: input.asin,
    www: input.www,
    cover,
    durationInSeconds: meta.durationInSeconds,
    fileInfo: meta.fileInfo,
  };
}
