namespace AudiobookManager.Database.Models;

public enum ConsistencyIssueType
{
    MissingMediaFile = 0,
    WrongFilePath = 1,
    MissingDescTxt = 2,
    IncorrectDescTxt = 3,
    MissingReaderTxt = 4,
    IncorrectReaderTxt = 5,
    MissingCoverFile = 6,
    TagMismatch = 7,
    MissingOpfFile = 8,
    IncorrectOpfFile = 9,

    /// <summary>
    /// The media file exists but could not be read - a truncated or corrupt m4b, a file ATL
    /// cannot parse, a permission-denied directory. Distinct from <see cref="MissingMediaFile"/>,
    /// which is resolved by deleting the library record: an unreadable file is still a file, and
    /// deleting the record would be the wrong answer to it.
    /// </summary>
    UnreadableFile = 10,

    /// <summary>
    /// The media file is missing *and* its parent directory is missing too - an unmounted
    /// subtree (a dead per-author or per-share mount) rather than a genuinely deleted book. The
    /// two look identical to <c>File.Exists</c>, but they are answered very differently:
    /// <see cref="MissingMediaFile"/> is resolved by deleting the library record, which is
    /// correct only when the parent directory still exists (deleting a book leaves its directory
    /// behind). A book whose whole directory has vanished must never be deleted on that evidence
    /// - the share may come back - so this state is resolved by looking again, like
    /// <see cref="UnreadableFile"/>.
    /// </summary>
    LibraryPathUnavailable = 11,

    /// <summary>
    /// The library-wide initials-spacing setting (<c>LibrarySettings.InitialsSpacing</c>) says the
    /// dotted initials in person names are either spaced ("J. K. Rowling") or unspaced
    /// ("J.K. Rowling"), and a stored author/narrator value does not follow it. Unlike the other
    /// issue types, this one is person-scoped rather than file-scoped: one issue per distinct
    /// non-compliant person value, with <see cref="ConsistencyIssue.AudiobookId"/> naming a
    /// representative book the person appears on, and <see cref="ConsistencyIssue.ExpectedValue"/>/
    /// <see cref="ConsistencyIssue.ActualValue"/> carrying the canonical vs stored spelling.
    /// Resolving rewrites the person value on every book that carries it via
    /// <c>AudiobookService.UpdateAudiobook</c>, never a DB-only field update.
    /// </summary>
    InitialsSpacingMismatch = 12,

    /// <summary>
    /// A metadata refresh (single or bulk) for this book failed - a network error, a scrape
    /// error, a source that no longer answers. Bookkeeping-only: the book's files and tags were
    /// never touched, so resolving means *retrying* the refresh (modeled on
    /// <see cref="ConsistencyIssueType.UnreadableFile"/>, where resolving is also just looking
    /// again). <see cref="ConsistencyIssue.ActualValue"/> carries the error message, and
    /// resolving a stale issue is not a data loss risk because the pending-refresh snapshot, if
    /// any, is separate state.
    /// </summary>
    MetadataRefreshFailed = 13,

    /// <summary>
    /// An owned book of a matched series stores a <c>SeriesPart</c> that is missing or differs
    /// from the position its matching roster entry assigns (the book was matched by title, but
    /// renumbered or never numbered). Detected from the same cached per-series reconciliation the
    /// series detail renders, so the two can never disagree. <see cref="ConsistencyIssue.ExpectedValue"/>
    /// carries the roster's position (which resolving writes into the m4b and database through the
    /// binding-invariant no-DB-only-update pipeline) and <see cref="ConsistencyIssue.ActualValue"/>
    /// the stored part; the description names the roster title the book was matched against.
    /// The series detail's Part Mismatches section reports the same findings one series at a time
    /// and fixes them through the existing expected-book-apply endpoint.
    /// </summary>
    SeriesPartMismatch = 14
}
