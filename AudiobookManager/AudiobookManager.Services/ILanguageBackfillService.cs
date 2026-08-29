namespace AudiobookManager.Services;

/// <summary>
/// Outcome of one backfill pass. <paramref name="Skipped"/> counts books whose m4b carries no
/// language tag, or one naming a language the library does not manage - those are deliberately
/// left empty so they stay visible under Missing Tags.
/// </summary>
public record LanguageBackfillResult(int Scanned, int Updated, int Skipped, int Failed);

public interface ILanguageBackfillService
{
    /// <summary>
    /// Populates the language of every book that has none from the value already embedded in its
    /// m4b, normalized to an ISO 639-1 code.
    ///
    /// The files themselves are left alone: a book whose tag reads "English" ends up recorded as
    /// "en", which the consistency check then reports as a TagMismatch, and resolving that
    /// rewrites the tag and metadata.opf. That is the intended sequence - it keeps this pass to a
    /// read of each file's header instead of a rewrite of every m4b in the library.
    /// </summary>
    Task<LanguageBackfillResult> BackfillFromTagsAsync(Func<string, int, int, Task> progressAction);
}
