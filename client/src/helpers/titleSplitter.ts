/**
 * Mirrors the backend's `AudiobookManager.Services.TitleSplitter.Apply` exactly: optionally
 * recovers a "Title: Subtitle" pair from a single title string. Splits `bookName` at the first
 * ": " (colon-space) only when `enabled` is true and `subtitle` is blank. A bare colon with no
 * following space (e.g. "4:50 from Paddington") is never treated as a separator - guessing wrong
 * there silently corrupts a perfectly good title, which is exactly the bug this replaces. Returns
 * the inputs unchanged when `enabled` is false, or no ": " separator exists in the title. When a
 * subtitle is already present the title is never re-split; only a title that ends in ": <that
 * subtitle>" loses the duplicated tail.
 */
export function splitTitleOnColon(
  bookName: string,
  subtitle: string | null | undefined,
  enabled: boolean,
): { bookName: string; subtitle: string | null | undefined } {
  if (!enabled) {
    return { bookName, subtitle };
  }

  const existingSubtitle = subtitle?.trim() ?? "";
  if (existingSubtitle.length > 0) {
    // The source already carries the subtitle AND repeats it in the title ("Title: Subtitle" with
    // subtitle "Subtitle"): drop the duplicated tail from the title, keep the subtitle as is.
    const suffix = `: ${existingSubtitle}`;
    const trimmedName = bookName.trimEnd();
    if (
      trimmedName.length > suffix.length &&
      trimmedName.toLowerCase().endsWith(suffix.toLowerCase())
    ) {
      return {
        bookName: trimmedName.slice(0, trimmedName.length - suffix.length).trim(),
        subtitle,
      };
    }
    return { bookName, subtitle };
  }

  const separatorIndex = bookName.indexOf(": ");
  if (separatorIndex < 0) {
    return { bookName, subtitle };
  }

  return {
    bookName: bookName.slice(0, separatorIndex).trim(),
    subtitle: bookName.slice(separatorIndex + 1).trim(),
  };
}
