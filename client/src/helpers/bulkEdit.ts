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
