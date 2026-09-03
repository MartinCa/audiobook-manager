using AudiobookManager.Database.Models;
using AudiobookManager.FileManager;

namespace AudiobookManager.Services;

public sealed class PathMismatchDetector : IConsistencyIssueDetector
{
    public IEnumerable<ConsistencyIssue> Detect(AudiobookCheckContext context)
    {
        var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(context.Parsed);
        var expectedFullPath = AudiobookFileHandler.JoinPaths(context.LibraryPath, expectedRelativePath);

        if (!AudiobookFileHandler.PathsEqual(context.Audiobook.FileInfoFullPath, expectedFullPath))
        {
            yield return ConsistencyIssueFactory.Create(context.Audiobook.Id, ConsistencyIssueType.WrongFilePath,
                "File path does not match expected path from tags",
                expectedFullPath, context.Audiobook.FileInfoFullPath);
        }
    }
}
