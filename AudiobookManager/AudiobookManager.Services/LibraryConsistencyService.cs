using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

public class LibraryConsistencyService : ILibraryConsistencyService
{
    private readonly AudiobookManagerSettings _settings;
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly ILogger<LibraryConsistencyService> _logger;

    public LibraryConsistencyService(
        IOptions<AudiobookManagerSettings> settings,
        IAudiobookRepository audiobookRepository,
        IConsistencyIssueRepository issueRepository,
        IAudiobookTagHandler tagHandler,
        ILogger<LibraryConsistencyService> logger)
    {
        _settings = settings.Value;
        _audiobookRepository = audiobookRepository;
        _issueRepository = issueRepository;
        _tagHandler = tagHandler;
        _logger = logger;
    }

    public async Task RunConsistencyCheck(Func<string, int, int, int, Task> progressAction)
    {
        _logger.LogInformation("Starting library consistency check");

        await _issueRepository.ClearAllAsync();

        var audiobooks = await _audiobookRepository.GetAllWithIncludesAsync();
        var totalBooks = audiobooks.Count;
        var booksChecked = 0;
        var issuesFound = 0;

        foreach (var audiobook in audiobooks)
        {
            booksChecked++;
            var bookLabel = $"{string.Join(", ", audiobook.Authors.Select(a => a.Name))} — {audiobook.BookName}";

            if (!File.Exists(audiobook.FileInfoFullPath))
            {
                await InsertIssue(audiobook.Id, ConsistencyIssueType.MissingMediaFile,
                    $"Media file not found: {audiobook.FileInfoFileName}",
                    audiobook.FileInfoFullPath, null);
                issuesFound++;
                await progressAction($"Missing: {bookLabel}", booksChecked, totalBooks, issuesFound);
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
                var parsed = _tagHandler.ParseAudiobook(fileInfo);

                // Check file path
                var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(parsed);
                var expectedFullPath = AudiobookFileHandler.JoinPaths(_settings.AudiobookLibraryPath, expectedRelativePath);
                if (!string.Equals(audiobook.FileInfoFullPath, expectedFullPath, StringComparison.Ordinal))
                {
                    await InsertIssue(audiobook.Id, ConsistencyIssueType.WrongFilePath,
                        "File path does not match expected path from tags",
                        expectedFullPath, audiobook.FileInfoFullPath);
                    issuesFound++;
                }

                var directoryPath = Path.GetDirectoryName(audiobook.FileInfoFullPath)!;

                // Check desc.txt
                var descPath = AudiobookFileHandler.JoinPaths(directoryPath, "desc.txt");
                if (!string.IsNullOrEmpty(parsed.Description))
                {
                    if (!File.Exists(descPath))
                    {
                        await InsertIssue(audiobook.Id, ConsistencyIssueType.MissingDescTxt,
                            "desc.txt missing but m4b has Description tag",
                            parsed.Description, null);
                        issuesFound++;
                    }
                    else
                    {
                        var descContent = await File.ReadAllTextAsync(descPath);
                        if (!string.Equals(descContent, parsed.Description, StringComparison.Ordinal))
                        {
                            await InsertIssue(audiobook.Id, ConsistencyIssueType.IncorrectDescTxt,
                                "desc.txt content does not match Description tag",
                                parsed.Description, descContent);
                            issuesFound++;
                        }
                    }
                }

                // Check reader.txt
                var readerPath = AudiobookFileHandler.JoinPaths(directoryPath, "reader.txt");
                if (parsed.Narrators.Any())
                {
                    var expectedNarrators = string.Join(", ", parsed.Narrators.Select(n => n.Name));
                    if (!File.Exists(readerPath))
                    {
                        await InsertIssue(audiobook.Id, ConsistencyIssueType.MissingReaderTxt,
                            "reader.txt missing but m4b has Narrators tag",
                            expectedNarrators, null);
                        issuesFound++;
                    }
                    else
                    {
                        var readerContent = await File.ReadAllTextAsync(readerPath);
                        if (!string.Equals(readerContent, expectedNarrators, StringComparison.Ordinal))
                        {
                            await InsertIssue(audiobook.Id, ConsistencyIssueType.IncorrectReaderTxt,
                                "reader.txt content does not match Narrators tag",
                                expectedNarrators, readerContent);
                            issuesFound++;
                        }
                    }
                }

                // Check cover file
                if (parsed.Cover is not null)
                {
                    var coverExists = File.Exists(AudiobookFileHandler.JoinPaths(directoryPath, "cover.jpg"))
                        || File.Exists(AudiobookFileHandler.JoinPaths(directoryPath, "cover.png"));
                    if (!coverExists)
                    {
                        await InsertIssue(audiobook.Id, ConsistencyIssueType.MissingCoverFile,
                            "Cover file missing but m4b has embedded cover",
                            "cover.jpg or cover.png", null);
                        issuesFound++;
                    }
                }

                await progressAction($"Checked: {bookLabel}", booksChecked, totalBooks, issuesFound);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check consistency for {FilePath}", audiobook.FileInfoFullPath);
                await progressAction($"Error checking: {bookLabel}", booksChecked, totalBooks, issuesFound);
            }
        }

        // Fuzzy check: similar author names
        await progressAction("Checking for similar author names...", booksChecked, totalBooks, issuesFound);
        var authorNameToBooks = new Dictionary<string, List<long>>();
        foreach (var audiobook in audiobooks)
        {
            foreach (var author in audiobook.Authors)
            {
                if (!authorNameToBooks.ContainsKey(author.Name))
                    authorNameToBooks[author.Name] = new List<long>();
                authorNameToBooks[author.Name].Add(audiobook.Id);
            }
        }
        issuesFound += await FindSimilarNames(authorNameToBooks, ConsistencyIssueType.SimilarAuthorNames, "Author");

        // Fuzzy check: similar series names
        await progressAction("Checking for similar series names...", booksChecked, totalBooks, issuesFound);
        var seriesNameToBooks = new Dictionary<string, List<long>>();
        foreach (var audiobook in audiobooks)
        {
            if (!string.IsNullOrEmpty(audiobook.Series))
            {
                if (!seriesNameToBooks.ContainsKey(audiobook.Series))
                    seriesNameToBooks[audiobook.Series] = new List<long>();
                seriesNameToBooks[audiobook.Series].Add(audiobook.Id);
            }
        }
        issuesFound += await FindSimilarNames(seriesNameToBooks, ConsistencyIssueType.SimilarSeriesNames, "Series");

        _logger.LogInformation("Consistency check complete. Books: {Total}, Issues: {Issues}", totalBooks, issuesFound);
    }

    public async Task ResolveIssue(long issueId)
    {
        var issue = await _issueRepository.GetByIdAsync(issueId);
        if (issue == null)
            throw new KeyNotFoundException($"Issue {issueId} not found");

        switch (issue.IssueType)
        {
            case ConsistencyIssueType.MissingMediaFile:
                await ResolveMissingMediaFile(issue);
                break;

            case ConsistencyIssueType.WrongFilePath:
                await ResolveWrongFilePath(issue);
                break;

            case ConsistencyIssueType.MissingDescTxt:
            case ConsistencyIssueType.IncorrectDescTxt:
            case ConsistencyIssueType.MissingReaderTxt:
            case ConsistencyIssueType.IncorrectReaderTxt:
                await ResolveMetadataIssue(issue);
                break;

            case ConsistencyIssueType.MissingCoverFile:
                await ResolveMissingCover(issue);
                break;

            case ConsistencyIssueType.SimilarAuthorNames:
            case ConsistencyIssueType.SimilarSeriesNames:
                await _issueRepository.DeleteAsync(issue.Id);
                break;
        }
    }

    private async Task ResolveMissingMediaFile(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var directoryPath = Path.GetDirectoryName(audiobook.FileInfoFullPath);

        await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
        await _audiobookRepository.DeleteAudiobookAsync(audiobook.Id);

        if (directoryPath != null)
        {
            AudiobookFileHandler.RemoveDirIfEmpty(directoryPath);
        }
    }

    private async Task ResolveWrongFilePath(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
        var parsed = _tagHandler.ParseAudiobook(fileInfo);

        var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(parsed);
        var expectedFullPath = AudiobookFileHandler.JoinPaths(_settings.AudiobookLibraryPath, expectedRelativePath);

        var oldDirectory = Path.GetDirectoryName(audiobook.FileInfoFullPath);

        AudiobookFileHandler.RelocateAudiobook(parsed, expectedFullPath);

        // Re-parse from new location
        var newFileInfo = new FileInfo(expectedFullPath);
        var newParsed = _tagHandler.ParseAudiobook(newFileInfo);

        AudiobookFileHandler.WriteMetadata(newParsed);
        var coverPath = AudiobookFileHandler.WriteCover(newParsed);

        var newFileName = Path.GetFileName(expectedFullPath);
        await _audiobookRepository.UpdateFilePathAsync(audiobook.Id, expectedFullPath, newFileName);
        await _audiobookRepository.UpdateCoverFilePathAsync(audiobook.Id, coverPath);

        if (oldDirectory != null)
        {
            AudiobookFileHandler.RemoveDirIfEmpty(oldDirectory);
        }

        // Path change invalidates all other checks for this book
        await _issueRepository.DeleteByAudiobookIdAsync(audiobook.Id);
    }

    private async Task ResolveMetadataIssue(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
        var parsed = _tagHandler.ParseAudiobook(fileInfo);

        AudiobookFileHandler.WriteMetadata(parsed);

        // Delete all desc/reader issues for this book since WriteMetadata writes both
        await _issueRepository.DeleteByAudiobookIdAndTypesAsync(audiobook.Id, new[]
        {
            ConsistencyIssueType.MissingDescTxt, ConsistencyIssueType.IncorrectDescTxt,
            ConsistencyIssueType.MissingReaderTxt, ConsistencyIssueType.IncorrectReaderTxt
        });
    }

    private async Task ResolveMissingCover(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;
        var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
        var parsed = _tagHandler.ParseAudiobook(fileInfo);

        var coverPath = AudiobookFileHandler.WriteCover(parsed);
        await _audiobookRepository.UpdateCoverFilePathAsync(audiobook.Id, coverPath);

        await _issueRepository.DeleteAsync(issue.Id);
    }

    public async Task<(int resolved, int failed)> ResolveIssuesByType(string issueType)
    {
        if (!Enum.TryParse<ConsistencyIssueType>(issueType, out var parsedType))
            throw new ArgumentException($"Unknown issue type: {issueType}");

        var issues = await _issueRepository.GetByTypeAsync(parsedType);
        var resolved = 0;
        var failed = 0;

        foreach (var issue in issues)
        {
            try
            {
                await ResolveIssue(issue.Id);
                resolved++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve issue {IssueId} during bulk resolve", issue.Id);
                failed++;
            }
        }

        return (resolved, failed);
    }

    private async Task<int> FindSimilarNames(
        Dictionary<string, List<long>> nameToBooks,
        ConsistencyIssueType issueType,
        string label)
    {
        var names = nameToBooks.Keys.ToList();
        var issuesFound = 0;
        var reportedPairs = new HashSet<(string, string)>();

        for (var i = 0; i < names.Count; i++)
        {
            for (var j = i + 1; j < names.Count; j++)
            {
                var name1 = names[i];
                var name2 = names[j];

                if (string.Equals(name1, name2, StringComparison.OrdinalIgnoreCase))
                    continue;

                var similarity = GetNormalizedSimilarity(name1, name2);
                if (similarity < 0.85)
                    continue;

                var pairKey = string.CompareOrdinal(name1, name2) < 0 ? (name1, name2) : (name2, name1);
                if (!reportedPairs.Add(pairKey))
                    continue;

                // Attach issue to the first audiobook of the less common name
                var count1 = nameToBooks[name1].Count;
                var count2 = nameToBooks[name2].Count;
                var (common, variant) = count1 >= count2 ? (name1, name2) : (name2, name1);
                var variantBookId = nameToBooks[variant].First();
                var commonCount = nameToBooks[common].Count;
                var variantCount = nameToBooks[variant].Count;

                var description = $"{label} name \"{variant}\" ({variantCount} book{(variantCount == 1 ? "" : "s")}) is similar to \"{common}\" ({commonCount} book{(commonCount == 1 ? "" : "s")})";

                await InsertIssue(variantBookId, issueType, description, common, variant);
                issuesFound++;
            }
        }

        return issuesFound;
    }

    internal static double GetNormalizedSimilarity(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
            return 1.0;

        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0)
            return 1.0;

        var distance = GetLevenshteinDistance(a.ToLowerInvariant(), b.ToLowerInvariant());
        return 1.0 - (double)distance / maxLen;
    }

    internal static int GetLevenshteinDistance(string a, string b)
    {
        var m = a.Length;
        var n = b.Length;

        var dp = new int[m + 1, n + 1];
        for (var i = 0; i <= m; i++) dp[i, 0] = i;
        for (var j = 0; j <= n; j++) dp[0, j] = j;

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[m, n];
    }

    private async Task InsertIssue(long audiobookId, ConsistencyIssueType issueType, string description, string? expectedValue, string? actualValue)
    {
        var issue = new ConsistencyIssue
        {
            AudiobookId = audiobookId,
            IssueType = issueType,
            Description = description,
            ExpectedValue = expectedValue,
            ActualValue = actualValue,
            DetectedAt = DateTime.UtcNow
        };
        await _issueRepository.InsertAsync(issue);
    }
}
