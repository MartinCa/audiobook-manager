using AudiobookManager.Database.Models;
using AudiobookManager.FileManager;

namespace AudiobookManager.Services;

/// <summary>
/// Checks desc.txt, reader.txt and metadata.opf together: all three are generated from the same
/// parsed tags and directory, and a resolve of any one of them (<c>WriteMetadata</c>) rewrites all
/// three at once - see <see cref="MetadataSidecarResolver"/>.
/// </summary>
public sealed class SidecarFilesDetector : IBookConsistencyIssueDetector
{
    public IEnumerable<BookConsistencyIssue> Detect(AudiobookCheckContext context)
    {
        foreach (var issue in DetectDescTxt(context)) yield return issue;
        foreach (var issue in DetectReaderTxt(context)) yield return issue;
        foreach (var issue in DetectOpfFile(context)) yield return issue;
    }

    // The "no tag at all" branch matters as much as the others: this sidecar is generated from the
    // tag and takes precedence over it in Audiobookshelf, so one left behind after its field was
    // cleared is a file actively serving stale metadata. It used to be invisible here - the whole
    // check was skipped when the tag was empty - and WriteMetadata never rewrote it either, so it
    // survived every save and every consistency run.
    private static IEnumerable<BookConsistencyIssue> DetectDescTxt(AudiobookCheckContext context)
    {
        var descPath = AudiobookFileHandler.JoinPaths(context.DirectoryPath, "desc.txt");

        if (!string.IsNullOrEmpty(context.Parsed.Description))
        {
            if (!File.Exists(descPath))
            {
                yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.MissingDescTxt,
                    "desc.txt missing but m4b has Description tag",
                    context.Parsed.Description, null);
            }
            else
            {
                var descContent = File.ReadAllText(descPath);
                if (!string.Equals(descContent, context.Parsed.Description, StringComparison.Ordinal))
                {
                    yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.IncorrectDescTxt,
                        "desc.txt content does not match Description tag",
                        context.Parsed.Description, descContent);
                }
            }
        }
        else if (File.Exists(descPath))
        {
            yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.IncorrectDescTxt,
                "desc.txt present but m4b has no Description tag",
                null, File.ReadAllText(descPath));
        }
    }

    private static IEnumerable<BookConsistencyIssue> DetectReaderTxt(AudiobookCheckContext context)
    {
        var readerPath = AudiobookFileHandler.JoinPaths(context.DirectoryPath, "reader.txt");

        if (context.Parsed.Narrators.Any())
        {
            var expectedNarrators = string.Join(", ", context.Parsed.Narrators.Select(n => n.Name));
            if (!File.Exists(readerPath))
            {
                yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.MissingReaderTxt,
                    "reader.txt missing but m4b has Narrators tag",
                    expectedNarrators, null);
            }
            else
            {
                var readerContent = File.ReadAllText(readerPath);
                if (!string.Equals(readerContent, expectedNarrators, StringComparison.Ordinal))
                {
                    yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.IncorrectReaderTxt,
                        "reader.txt content does not match Narrators tag",
                        expectedNarrators, readerContent);
                }
            }
        }
        else if (File.Exists(readerPath))
        {
            yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.IncorrectReaderTxt,
                "reader.txt present but m4b has no Narrators tag",
                null, File.ReadAllText(readerPath));
        }
    }

    // Unlike desc.txt/reader.txt, metadata.opf is expected unconditionally once the book has tags
    // at all, since it always carries at least the title/authors.
    private static IEnumerable<BookConsistencyIssue> DetectOpfFile(AudiobookCheckContext context)
    {
        var opfPath = AudiobookFileHandler.JoinPaths(context.DirectoryPath, "metadata.opf");
        var expectedOpfContent = AudiobookFileHandler.BuildOpfContent(context.Parsed);

        if (!File.Exists(opfPath))
        {
            yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.MissingOpfFile,
                "metadata.opf missing",
                expectedOpfContent, null);
        }
        else
        {
            var opfContent = File.ReadAllText(opfPath);
            if (!string.Equals(opfContent, expectedOpfContent, StringComparison.Ordinal))
            {
                yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.IncorrectOpfFile,
                    "metadata.opf content does not match library metadata",
                    expectedOpfContent, opfContent);
            }
        }
    }
}
