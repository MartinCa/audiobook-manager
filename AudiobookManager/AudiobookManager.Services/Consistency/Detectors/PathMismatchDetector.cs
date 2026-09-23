using AudiobookManager.Database.Models;
using AudiobookManager.FileManager;

namespace AudiobookManager.Services;

public sealed class PathMismatchDetector : IBookConsistencyIssueDetector
{
    public IEnumerable<BookConsistencyIssue> Detect(AudiobookCheckContext context)
    {
        var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(context.Parsed);
        // Same join as AudiobookService.GenerateLibraryPath, and it has to stay the same one: this
        // is the path a WrongFilePath resolve will move the file to, so a path this check would
        // accept but the organize path would refuse is a loop the user cannot resolve.
        var expectedFullPath = AudiobookFileHandler.JoinLibraryPath(context.LibraryPath, expectedRelativePath);

        if (!AudiobookFileHandler.PathsEqual(context.Audiobook.FileInfoFullPath, expectedFullPath))
        {
            yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.WrongFilePath,
                "File path does not match expected path from tags",
                expectedFullPath, context.Audiobook.FileInfoFullPath);
        }
    }
}
