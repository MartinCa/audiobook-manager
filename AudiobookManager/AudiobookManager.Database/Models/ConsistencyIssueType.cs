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
    UnreadableFile = 10
}
