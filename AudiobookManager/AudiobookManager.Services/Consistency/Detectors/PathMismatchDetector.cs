using AudiobookManager.Database.Models;
using AudiobookManager.FileManager;

namespace AudiobookManager.Services;

public sealed class PathMismatchDetector : IBookConsistencyIssueDetector
{
    public IEnumerable<BookConsistencyIssue> Detect(AudiobookCheckContext context)
    {
        var (expectedFullPath, tooLong) = TryGenerateExpectedPath(context);

        if (tooLong is not null)
        {
            // The book's tags would need a folder or file name the file system cannot hold, so
            // there is no path to compare against. That is a verdict about the metadata of a
            // perfectly readable file, and it must not escape: it would reach the broad catch in
            // AudiobookIssueDetectionService, label the book UnreadableFile and discard every
            // other check's result. It stays a WrongFilePath (the file is not where its tags say
            // it belongs, and cannot be until they are shortened); a resolve fails with the same
            // message, which is what tells the user what to shorten.
            yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.WrongFilePath,
                tooLong, null, context.Audiobook.FileInfoFullPath);
            yield break;
        }

        if (!AudiobookFileHandler.PathsEqual(context.Audiobook.FileInfoFullPath, expectedFullPath!))
        {
            yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.WrongFilePath,
                "File path does not match expected path from tags",
                expectedFullPath, context.Audiobook.FileInfoFullPath);
        }
    }

    // Not inline in Detect: an iterator cannot yield from inside a try block that has a catch.
    private static (string? ExpectedFullPath, string? TooLongMessage) TryGenerateExpectedPath(AudiobookCheckContext context)
    {
        try
        {
            var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(context.Parsed, context.MaxNarratorsInPath);
            // Same join as AudiobookService.GenerateLibraryPath, and it has to stay the same one: this
            // is the path a WrongFilePath resolve will move the file to, so a path this check would
            // accept but the organize path would refuse is a loop the user cannot resolve.
            return (AudiobookFileHandler.JoinLibraryPath(context.LibraryPath, expectedRelativePath), null);
        }
        catch (PathSegmentTooLongException ex)
        {
            return (null, ex.Message);
        }
    }
}
