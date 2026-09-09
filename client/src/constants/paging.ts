/**
 * Page sizes and list limits shared across the client.
 *
 * `BROWSE_PAGE_SIZE` and `SEARCH_PREVIEW_LIMIT` are the browse/search page defaults; they are a
 * client-side choice for how many rows to request and are not claimed to mirror a backend
 * constant (the browse endpoints accept any limit). `TYPEAHEAD_LIMIT` is the one value that does
 * track the backend: `GET /browse/library-search` defaults to 5, so the type-ahead search asks
 * for that many rows.
 */

/** Default page size for the main library lists (audiobooks, authors, series, missing tags). */
export const PAGE_SIZE = 50;

/** Rows per page on the browse/search result tabs ("books", "authors", "series"). */
export const BROWSE_PAGE_SIZE = 20;

/** Preview rows each section shows in the combined "all" search tab. */
export const SEARCH_PREVIEW_LIMIT = 5;

/**
 * Row limit for a type-ahead search hit (`GET /browse/library-search` defaults to this too —
 * the backend's default is 5). Matched values are only used to offer suggestions, so a small
 * bound is the point.
 */
export const TYPEAHEAD_LIMIT = 5;

/** How many type-ahead suggestions the client renders for a typed query. */
export const TYPEAHEAD_SUGGESTION_COUNT = 6;
