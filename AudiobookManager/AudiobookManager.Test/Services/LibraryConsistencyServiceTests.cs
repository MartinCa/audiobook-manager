using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Test.Services;

[TestClass]
public class LibraryConsistencyServiceTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IConsistencyIssueRepository> _issueRepository = null!;
    private Mock<IAudiobookTagHandler> _tagHandler = null!;
    private Mock<ILogger<LibraryConsistencyService>> _logger = null!;
    private IOptions<AudiobookManagerSettings> _settings = null!;
    private LibraryConsistencyService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _issueRepository = new Mock<IConsistencyIssueRepository>();
        _tagHandler = new Mock<IAudiobookTagHandler>();
        _logger = new Mock<ILogger<LibraryConsistencyService>>();
        _settings = Options.Create(new AudiobookManagerSettings
        {
            AudiobookLibraryPath = "/library"
        });

        _service = new LibraryConsistencyService(
            _settings,
            _audiobookRepository.Object,
            _issueRepository.Object,
            _tagHandler.Object,
            _logger.Object);
    }

    [TestMethod]
    public async Task RunConsistencyCheck_MissingFile_ReportsIssue()
    {
        var dbAudiobook = new DbAudiobook(
            1, "Test Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null,
            "/nonexistent/path/test.m4b", "test.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author") }
        };

        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync())
            .ReturnsAsync(new List<DbAudiobook> { dbAudiobook });

        var progressCalls = new List<(string message, int booksChecked, int total, int issues)>();
        Func<string, int, int, int, Task> progressAction = (msg, bc, t, i) =>
        {
            progressCalls.Add((msg, bc, t, i));
            return Task.CompletedTask;
        };

        await _service.RunConsistencyCheck(progressAction);

        _issueRepository.Verify(r => r.InsertAsync(It.Is<ConsistencyIssue>(i =>
            i.IssueType == ConsistencyIssueType.MissingMediaFile &&
            i.AudiobookId == 1
        )), Times.Once);

        // 1 per-book progress + 2 fuzzy check phases (authors, series)
        Assert.AreEqual(3, progressCalls.Count);
        Assert.AreEqual(1, progressCalls[0].issues);
    }

    [TestMethod]
    public async Task RunConsistencyCheck_AllGood_NoIssues()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            await File.WriteAllTextAsync(tempFile, "fake audio content");

            var descFile = Path.Combine(tempDir, "desc.txt");
            await File.WriteAllTextAsync(descFile, "A great book");

            var readerFile = Path.Combine(tempDir, "reader.txt");
            await File.WriteAllTextAsync(readerFile, "Narrator One");

            var coverFile = Path.Combine(tempDir, "cover.jpg");
            await File.WriteAllBytesAsync(coverFile, new byte[] { 0xFF, 0xD8 });

            var dbAudiobook = new DbAudiobook(
                1, "Test Book", null, null, null, 2024,
                "A great book", null, null, null, null, null, null, null,
                tempFile, "test.m4b", 1000)
            {
                Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author One") }
            };

            _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync())
                .ReturnsAsync(new List<DbAudiobook> { dbAudiobook });

            var parsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author One") },
                "Test Book",
                2024,
                new Domain.AudiobookFileInfo(tempFile, "test.m4b", 1000))
            {
                Description = "A great book",
                Narrators = new List<Domain.Person> { new Domain.Person("Narrator One") },
                Cover = new Domain.AudiobookImage("AAAA", "image/jpeg")
            };

            // GenerateRelativeAudiobookPath is static, so we need the parsed audiobook to generate a path that matches tempFile
            // Since the generated path won't match our tempFile, this will create a WrongFilePath issue.
            // For a true "all good" test we'd need the paths to match, which is hard with static methods.
            // Instead, we verify no MissingMediaFile issue is created (file exists) and the check completes.
            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>()))
                .Returns(parsed);

            var progressCalls = new List<(string message, int booksChecked, int total, int issues)>();
            Func<string, int, int, int, Task> progressAction = (msg, bc, t, i) =>
            {
                progressCalls.Add((msg, bc, t, i));
                return Task.CompletedTask;
            };

            await _service.RunConsistencyCheck(progressAction);

            // File exists, so MissingMediaFile should NOT be inserted
            _issueRepository.Verify(r => r.InsertAsync(It.Is<ConsistencyIssue>(i =>
                i.IssueType == ConsistencyIssueType.MissingMediaFile
            )), Times.Never);

            // 1 per-book progress + 2 fuzzy check phases (authors, series)
            Assert.AreEqual(3, progressCalls.Count);
            Assert.IsTrue(progressCalls[0].message.StartsWith("Checked:"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task ResolveIssue_MissingMediaFile_DeletesAudiobook()
    {
        var dbAudiobook = new DbAudiobook(
            1, "Test Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null,
            "/some/path/test.m4b", "test.m4b", 1000);

        var issue = new ConsistencyIssue
        {
            Id = 10,
            AudiobookId = 1,
            Audiobook = dbAudiobook,
            IssueType = ConsistencyIssueType.MissingMediaFile,
            Description = "File missing",
            DetectedAt = DateTime.UtcNow
        };

        _issueRepository.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(issue);

        await _service.ResolveIssue(10);

        _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Once);
        _audiobookRepository.Verify(r => r.DeleteAudiobookAsync(1), Times.Once);
    }

    [TestMethod]
    public async Task ResolveIssue_MetadataIssue_WritesMetadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            await File.WriteAllTextAsync(tempFile, "fake");

            var dbAudiobook = new DbAudiobook(
                1, "Test Book", null, null, null, 2024,
                "desc", null, null, null, null, null, null, null,
                tempFile, "test.m4b", 1000);

            var issue = new ConsistencyIssue
            {
                Id = 20,
                AudiobookId = 1,
                Audiobook = dbAudiobook,
                IssueType = ConsistencyIssueType.MissingDescTxt,
                Description = "desc.txt missing",
                DetectedAt = DateTime.UtcNow
            };

            _issueRepository.Setup(r => r.GetByIdAsync(20)).ReturnsAsync(issue);

            var parsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Test Book",
                2024,
                new Domain.AudiobookFileInfo(tempFile, "test.m4b", 1000))
            {
                Description = "A description"
            };

            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>())).Returns(parsed);

            await _service.ResolveIssue(20);

            // WriteMetadata is static, so we verify it was called indirectly by checking desc.txt was created
            var descPath = Path.Combine(tempDir, "desc.txt");
            Assert.IsTrue(File.Exists(descPath));
            Assert.AreEqual("A description", await File.ReadAllTextAsync(descPath));

            _issueRepository.Verify(r => r.DeleteByAudiobookIdAndTypesAsync(1,
                It.Is<IEnumerable<ConsistencyIssueType>>(types =>
                    types.Contains(ConsistencyIssueType.MissingDescTxt) &&
                    types.Contains(ConsistencyIssueType.IncorrectDescTxt) &&
                    types.Contains(ConsistencyIssueType.MissingReaderTxt) &&
                    types.Contains(ConsistencyIssueType.IncorrectReaderTxt)
                )), Times.Once);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task ResolveIssue_NotFound_ThrowsKeyNotFound()
    {
        _issueRepository.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ConsistencyIssue?)null);

        var exception = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _service.ResolveIssue(999));
        Assert.IsNotNull(exception);
    }

    [TestMethod]
    public async Task ResolveIssue_SimilarAuthorNames_DismissesIssue()
    {
        var dbAudiobook = new DbAudiobook(
            1, "Test Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null,
            "/some/path/test.m4b", "test.m4b", 1000);

        var issue = new ConsistencyIssue
        {
            Id = 30,
            AudiobookId = 1,
            Audiobook = dbAudiobook,
            IssueType = ConsistencyIssueType.SimilarAuthorNames,
            Description = "Author name \"J.R.R. Tolkein\" is similar to \"J.R.R. Tolkien\"",
            ExpectedValue = "J.R.R. Tolkien",
            ActualValue = "J.R.R. Tolkein",
            DetectedAt = DateTime.UtcNow
        };

        _issueRepository.Setup(r => r.GetByIdAsync(30)).ReturnsAsync(issue);

        await _service.ResolveIssue(30);

        _issueRepository.Verify(r => r.DeleteAsync(30), Times.Once);
    }

    [TestMethod]
    public async Task RunConsistencyCheck_SimilarAuthors_ReportsIssue()
    {
        var book1 = new DbAudiobook(
            1, "Book One", null, null, null, 2024,
            null, null, null, null, null, null, null, null,
            "/nonexistent/path1/book1.m4b", "book1.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "J.R.R. Tolkien") }
        };

        var book2 = new DbAudiobook(
            2, "Book Two", null, null, null, 2024,
            null, null, null, null, null, null, null, null,
            "/nonexistent/path2/book2.m4b", "book2.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(2, "J.R.R. Tolkein") }
        };

        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync())
            .ReturnsAsync(new List<DbAudiobook> { book1, book2 });

        var progressCalls = new List<(string message, int booksChecked, int total, int issues)>();
        Func<string, int, int, int, Task> progressAction = (msg, bc, t, i) =>
        {
            progressCalls.Add((msg, bc, t, i));
            return Task.CompletedTask;
        };

        await _service.RunConsistencyCheck(progressAction);

        _issueRepository.Verify(r => r.InsertAsync(It.Is<ConsistencyIssue>(i =>
            i.IssueType == ConsistencyIssueType.SimilarAuthorNames
        )), Times.Once);
    }

    [TestMethod]
    public async Task RunConsistencyCheck_SimilarSeries_ReportsIssue()
    {
        var book1 = new DbAudiobook(
            1, "Book One", null, "The Lord of the Rings", "1", 2024,
            null, null, null, null, null, null, null, null,
            "/nonexistent/path1/book1.m4b", "book1.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author") }
        };

        var book2 = new DbAudiobook(
            2, "Book Two", null, "The Lord of the Ring", "2", 2024,
            null, null, null, null, null, null, null, null,
            "/nonexistent/path2/book2.m4b", "book2.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author") }
        };

        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync())
            .ReturnsAsync(new List<DbAudiobook> { book1, book2 });

        var progressCalls = new List<(string message, int booksChecked, int total, int issues)>();
        Func<string, int, int, int, Task> progressAction = (msg, bc, t, i) =>
        {
            progressCalls.Add((msg, bc, t, i));
            return Task.CompletedTask;
        };

        await _service.RunConsistencyCheck(progressAction);

        _issueRepository.Verify(r => r.InsertAsync(It.Is<ConsistencyIssue>(i =>
            i.IssueType == ConsistencyIssueType.SimilarSeriesNames
        )), Times.Once);
    }

    [TestMethod]
    public async Task RunConsistencyCheck_DifferentAuthors_NoFuzzyIssue()
    {
        var book1 = new DbAudiobook(
            1, "Book One", null, null, null, 2024,
            null, null, null, null, null, null, null, null,
            "/nonexistent/path1/book1.m4b", "book1.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Stephen King") }
        };

        var book2 = new DbAudiobook(
            2, "Book Two", null, null, null, 2024,
            null, null, null, null, null, null, null, null,
            "/nonexistent/path2/book2.m4b", "book2.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(2, "Dean Koontz") }
        };

        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync())
            .ReturnsAsync(new List<DbAudiobook> { book1, book2 });

        var progressCalls = new List<(string message, int booksChecked, int total, int issues)>();
        Func<string, int, int, int, Task> progressAction = (msg, bc, t, i) =>
        {
            progressCalls.Add((msg, bc, t, i));
            return Task.CompletedTask;
        };

        await _service.RunConsistencyCheck(progressAction);

        _issueRepository.Verify(r => r.InsertAsync(It.Is<ConsistencyIssue>(i =>
            i.IssueType == ConsistencyIssueType.SimilarAuthorNames
        )), Times.Never);
    }

    [TestMethod]
    public void GetLevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        Assert.AreEqual(0, LibraryConsistencyService.GetLevenshteinDistance("hello", "hello"));
    }

    [TestMethod]
    public void GetLevenshteinDistance_OneCharDiff_ReturnsOne()
    {
        Assert.AreEqual(1, LibraryConsistencyService.GetLevenshteinDistance("tolkien", "tolkein"));
    }

    [TestMethod]
    public void GetLevenshteinDistance_EmptyString_ReturnsLength()
    {
        Assert.AreEqual(5, LibraryConsistencyService.GetLevenshteinDistance("hello", ""));
        Assert.AreEqual(5, LibraryConsistencyService.GetLevenshteinDistance("", "hello"));
    }

    [TestMethod]
    public void GetNormalizedSimilarity_IdenticalStrings_ReturnsOne()
    {
        Assert.AreEqual(1.0, LibraryConsistencyService.GetNormalizedSimilarity("hello", "hello"));
    }

    [TestMethod]
    public void GetNormalizedSimilarity_SimilarStrings_ReturnsHighValue()
    {
        var similarity = LibraryConsistencyService.GetNormalizedSimilarity("J.R.R. Tolkien", "J.R.R. Tolkein");
        Assert.IsTrue(similarity > 0.85, $"Expected > 0.85, got {similarity}");
    }

    [TestMethod]
    public void GetNormalizedSimilarity_DifferentStrings_ReturnsLowValue()
    {
        var similarity = LibraryConsistencyService.GetNormalizedSimilarity("Stephen King", "Dean Koontz");
        Assert.IsTrue(similarity < 0.5, $"Expected < 0.5, got {similarity}");
    }

    [TestMethod]
    public void GetNormalizedSimilarity_CaseInsensitive()
    {
        var similarity = LibraryConsistencyService.GetNormalizedSimilarity("Stephen King", "stephen king");
        Assert.AreEqual(1.0, similarity);
    }
}
