import { joinPersons } from "@/helpers/bookDetailsHelpers";
import type { Audiobook } from "@/types/Audiobook";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";

/**
 * Builds the tag-preview-diff shape (see OrganizeAudiobookInput's own comment) directly from a
 * loaded book, for a diff view with no mounted edit form to read live values from - the
 * metadata-refresh list's per-row quick apply, unlike BookEditForm's own currentOrganizeInput,
 * which reads its react-hook-form watch instead.
 */
export function audiobookToOrganizeInput(book: Audiobook): OrganizeAudiobookInput {
  return {
    authors: joinPersons(book.authors),
    narrators: joinPersons(book.narrators),
    bookName: book.bookName,
    subtitle: book.subtitle,
    series: book.series,
    seriesPart: book.seriesPart,
    year: book.year,
    genres: book.genres?.join("/"),
    description: book.description,
    copyright: book.copyright,
    publisher: book.publisher,
    language: book.language,
    www: book.www,
    rating: book.rating ? Number(book.rating) : undefined,
    asin: book.asin,
  };
}
