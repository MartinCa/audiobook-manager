import type { BulkEditPreviewBook } from "@/types/BulkEdit";

export interface BulkFieldState {
  /**
   * The single value every selected book shares — the empty string when every book is empty —
   * or null when the books disagree.
   */
  common: string | null;
  mixed: boolean;
}

export interface BulkMultiFieldState {
  /**
   * The exact ordered sequence every selected book shares (same order, case-sensitive), or null
   * when the books disagree.
   */
  common: string[] | null;
  mixed: boolean;
}

/**
 * Reduces one single-value field across the preview rows. Exactly one distinct value across all
 * books — including "every book is empty" — is common; anything else is a mixed state with no
 * common value to prefill. Accent-insensitivity is deliberately NOT applied: this is data
 * equality (what can be written in one bulk request), not search matching.
 */
export function computeSingleFieldState(
  books: BulkEditPreviewBook[],
  pick: (book: BulkEditPreviewBook) => string | null | undefined,
): BulkFieldState {
  if (books.length === 0) {
    return { common: null, mixed: false };
  }
  const distinct = new Set(books.map((book) => pick(book) ?? ""));
  if (distinct.size === 1) {
    return { common: distinct.values().next().value ?? "", mixed: false };
  }
  return { common: null, mixed: true };
}

/**
 * Reduces one multi-value field across the preview rows. The books agree only when every
 * sequence is exactly equal element-for-element in the same order, case-sensitive (a reordered
 * or re-worded list is a real difference the bulk request would resolve differently on rebuild).
 */
export function computeMultiFieldState(
  books: BulkEditPreviewBook[],
  pick: (book: BulkEditPreviewBook) => string[] | undefined,
): BulkMultiFieldState {
  if (books.length === 0) {
    return { common: null, mixed: false };
  }
  const first = pick(books[0]!) ?? [];
  const allEqual = books.every((book) => {
    const values = pick(book) ?? [];
    return values.length === first.length && values.every((value, i) => value === first[i]);
  });
  if (allEqual) {
    return { common: [...first], mixed: false };
  }
  return { common: null, mixed: true };
}

/**
 * The values every selected book has, in the first book's order and spelling. A value counts once
 * even if a book repeats it. Empty when nothing is in common, or nothing is selected. `same` decides
 * whether two spellings are the one value, which differs by field (see the two callers).
 */
function computeSharedValues(
  books: BulkEditPreviewBook[],
  valuesOf: (book: BulkEditPreviewBook) => string[],
  same: (a: string, b: string) => boolean,
): string[] {
  const [first, ...rest] = books;
  if (!first) {
    return [];
  }
  const shared: string[] = [];
  for (const value of valuesOf(first)) {
    if (shared.some((s) => same(s, value))) {
      continue;
    }
    if (rest.every((book) => valuesOf(book).some((v) => same(v, value)))) {
      shared.push(value);
    }
  }
  return shared;
}

/** One value a bulk "set one value" field can offer, and whether every selected book already has it set. */
export interface SharedChoiceOption {
  value: string;
  /**
   * True when the value is already what every selected book is set to (the main author, or the primary
   * series), so choosing it would change nothing. It is still listed - as a disabled row - so a user who
   * expects to see it learns why it cannot be picked, instead of finding it missing.
   */
  alreadySet: boolean;
}

/**
 * The authors every selected book is credited to - what a bulk "set main author" can offer, because
 * a main author is always one of a book's own authors. Names compare exactly (case-sensitive), the
 * way the backend keys people and checks the main author, so a "Jane Doe" and a "jane doe" are
 * different people here and an offered name never fails on a book.
 */
export function computeSharedAuthors(books: BulkEditPreviewBook[]): SharedChoiceOption[] {
  return computeSharedValues(
    books,
    (book) => book.authors,
    (a, b) => a === b,
  ).map((value) => ({
    value,
    alreadySet: books.every((book) => book.mainAuthor === value),
  }));
}

/** Two series names are the same series when trimmed and equal ignoring case (the server's rule). */
function sameSeries(a: string, b: string): boolean {
  return a.trim().toLowerCase() === b.trim().toLowerCase();
}

function seriesOf(book: BulkEditPreviewBook): string[] {
  const primary = book.series?.trim();
  return [...(primary ? [primary] : []), ...book.additionalSeries];
}

/**
 * The series a bulk "make primary" can offer: ones every selected book is in (as its primary or an
 * additional series - the action only reorders a book's own series, it never adds one), in the first
 * book's order and spelling. One that is already the primary of every selected book is flagged
 * `alreadySet`, since choosing it would change nothing.
 */
export function computeSharedSeries(books: BulkEditPreviewBook[]): SharedChoiceOption[] {
  return computeSharedValues(books, seriesOf, sameSeries).map((value) => ({
    value,
    alreadySet: books.every((book) => Boolean(book.series) && sameSeries(book.series!, value)),
  }));
}
