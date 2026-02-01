namespace AudiobookManager.Database.Models;

public enum ConsistencyIssueType
{
    MissingMediaFile = 0,
    WrongFilePath = 1,
    MissingDescTxt = 2,
    IncorrectDescTxt = 3,
    MissingReaderTxt = 4,
    IncorrectReaderTxt = 5,
    MissingCoverFile = 6
}
