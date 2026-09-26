/**
 * The single fallback/priority rule for the default query used to search online metadata
 * sources. Every UI entry point that seeds a metadata search must build its query through this
 * helper rather than re-deriving its own precedence, so the interactive search dialog and any
 * other caller never drift from each other or from the backend's mirrored
 * `MetadataSearchQueryBuilder` (used by the bulk online-match search).
 *
 * Priority, highest to lowest, falling back to the next when the higher one is blank:
 * 1. "Author - Book name" (authors joined with ", ", matching the display convention used
 *    elsewhere, e.g. MetadataSearchResultCard)
 * 2. Book name
 * 3. File name
 */
export function buildDefaultMetadataSearchQuery(
  authors: readonly string[] | undefined,
  bookName: string | undefined,
  fileName: string | undefined,
): string {
  const trimmedBookName = bookName?.trim() ?? "";
  const authorNames = (authors ?? []).map((a) => a.trim()).filter(Boolean);

  if (authorNames.length > 0 && trimmedBookName) {
    return `${authorNames.join(", ")} - ${trimmedBookName}`;
  }

  if (trimmedBookName) {
    return trimmedBookName;
  }

  return fileName?.trim() ?? "";
}
