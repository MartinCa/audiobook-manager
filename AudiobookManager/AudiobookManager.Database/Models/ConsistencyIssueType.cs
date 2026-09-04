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
    LibraryPathUnavailable = 11
}
