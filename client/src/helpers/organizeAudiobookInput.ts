import OrganizeAudiobookInput from "../types/OrganizeAudiobookInput";
import { Audiobook, AudiobookImage } from "../types/Audiobook";
import BookFileInfo from "../types/BookFileInfo";

// Shared by BookOrganize.vue and library/BookDetail.vue - both edit the same
// OrganizeAudiobookInput shape and need to turn it back into the Audiobook shape the
// generate-path/save endpoints expect. Only the duration/fileInfo, which come from whichever
// already-loaded book the form is editing, differ per caller.
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
    authors: input.authors?.split(",").map((x) => ({ name: x.trim() })) ?? [],
    narrators:
      input.narrators?.split(",").map((x) => ({ name: x.trim() })) ?? [],
    bookName: input.bookName,
    subtitle: input.subtitle,
    series: input.series,
    seriesPart: input.seriesPart,
    year: input.year,
    genres: input.genres?.split("/") ?? [],
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
