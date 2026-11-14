/**
 * A book's main author is one of its authors, stored by name. These helpers are the one place that
 * rule lives on the client, so the edit form, the save payload and the search seed agree - and so
 * a name that is no longer among the authors (removed or renamed in the same edit) never travels.
 */

/** Value of the "no main author" select item; a real author is never called this. */
export const NO_MAIN_AUTHOR = "__no-main-author__";

/**
 * The main author if it is still one of `authors` (exact name match - the backend keys people by
 * exact name), otherwise `undefined`. Blank counts as none.
 */
export function mainAuthorOf(
  values: { authors?: readonly string[]; mainAuthor?: string } | undefined,
): string | undefined {
  const main = values?.mainAuthor?.trim();
  if (!main) return undefined;
  return (values?.authors ?? []).includes(main) ? main : undefined;
}

/**
 * The form-state spelling of the main author after the authors changed: the same name while it is
 * still an author, otherwise empty (none). Returns a string because form fields hold "" for none.
 */
export function keepMainAuthor(mainAuthor: string | undefined, authors: readonly string[]): string {
  return mainAuthorOf({ authors, mainAuthor }) ?? "";
}

/** Whether the field is worth showing: several authors to pick from, or one already chosen. */
export function canChooseMainAuthor(authors: readonly string[], mainAuthor: string | undefined) {
  const named = authors.filter((a) => a.trim().length > 0);
  return named.length > 1 || mainAuthorOf({ authors, mainAuthor }) !== undefined;
}
