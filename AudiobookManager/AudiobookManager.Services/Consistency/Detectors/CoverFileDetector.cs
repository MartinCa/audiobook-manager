using AudiobookManager.Database.Models;
using AudiobookManager.FileManager;

namespace AudiobookManager.Services;

public sealed class CoverFileDetector : IConsistencyIssueDetector
{
    public IEnumerable<ConsistencyIssue> Detect(AudiobookCheckContext context)
    {
        var coverJpgExists = File.Exists(AudiobookFileHandler.JoinPaths(context.DirectoryPath, "cover.jpg"));
        var coverPngExists = File.Exists(AudiobookFileHandler.JoinPaths(context.DirectoryPath, "cover.png"));

        if (coverJpgExists && coverPngExists)
        {
            yield return ConsistencyIssueFactory.Create(context.Audiobook.Id, ConsistencyIssueType.MissingCoverFile,
                "Conflicting cover files (both cover.jpg and cover.png exist)",
                context.Parsed.Cover?.MimeType == "image/png" ? "cover.png" : "cover.jpg",
                "both cover.jpg and cover.png exist");
        }
        else if (context.Parsed.Cover is not null && !coverJpgExists && !coverPngExists)
        {
            yield return ConsistencyIssueFactory.Create(context.Audiobook.Id, ConsistencyIssueType.MissingCoverFile,
                "Cover file missing but m4b has embedded cover",
                "cover.jpg or cover.png", null);
        }
    }
}
