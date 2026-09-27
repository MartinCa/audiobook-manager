/**
 * Whether a string is an absolute http(s) URL, as opposed to a plain search query. Used by
 * BookSearchDialog to route a pasted book URL (e.g. "https://hardcover.app/books/1984") straight
 * to `metadataSearchApi.getBookDetails()` instead of the multi-source text search - see
 * AGENTS.md's "Adding a metadata source scraper" section.
 */
export function isAbsoluteHttpUrl(value: string): boolean {
  let parsed: URL;
  try {
    parsed = new URL(value);
  } catch {
    return false;
  }

  return parsed.protocol === "http:" || parsed.protocol === "https:";
}
