/**
 * Mirrors the backend's `AudiobookManager.Services.TitleSplitter.Apply` exactly: optionally
 * recovers a "Title: Subtitle" pair from a single title string. Splits `bookName` at the first
 * ": " (colon-space) only when `enabled` is true and `subtitle` is blank. A bare colon with no
 * following space (e.g. "4:50 from Paddington") is never treated as a separator - guessing wrong
 * there silently corrupts a perfectly good title, which is exactly the bug this replaces. Returns
 * the inputs unchanged when `enabled` is false, a subtitle is already present, or no ": "
 * separator exists in the title.
 */
export function splitTitleOnColon(
  bookName: string,
  subtitle: string | null | undefined,
  enabled: boolean,
): { bookName: string; subtitle: string | null | undefined } {
  if (!enabled || (subtitle != null && subtitle.trim().length > 0)) {
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
