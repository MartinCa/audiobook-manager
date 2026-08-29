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
    private Mock<IOrphanDirectoryRepository> _orphanDirectoryRepository = null!;
    private Mock<IAudiobookTagHandler> _tagHandler = null!;
    private Mock<IAudiobookService> _audiobookService = null!;
    private Mock<ILogger<LibraryConsistencyService>> _logger = null!;
    private IOptions<AudiobookManagerSettings> _settings = null!;
    private LibraryConsistencyService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _issueRepository = new Mock<IConsistencyIssueRepository>();
        _orphanDirectoryRepository = new Mock<IOrphanDirectoryRepository>();
        _tagHandler = new Mock<IAudiobookTagHandler>();
        _audiobookService = new Mock<IAudiobookService>();
        _logger = new Mock<ILogger<LibraryConsistencyService>>();
        _settings = Options.Create(new AudiobookManagerSettings
        {
            AudiobookLibraryPath = "/library"
        });

        _service = new LibraryConsistencyService(
            _settings,
            _audiobookRepository.Object,
            _issueRepository.Object,
            _orphanDirectoryRepository.Object,
            _tagHandler.Object,
            _audiobookService.Object,
            _logger.Object);
    }

    #region Bulk resolve cascades

    private static DbAudiobook MakeMissingFileBook(long id, string path) => new(
        id, $"Book {id}", null, null, null, 2024,
        null, null, null, null, null, null, null, null, null,
        path, Path.GetFileName(path), 1000)
    {
        Authors = new List<Database.Models.Person> { new(1, "Author") }
    };

    // Regression test: resolving one issue deletes every *other* stored issue for the same book
    // (a path change or tag rewrite invalidates all of them). The bulk resolve re-fetched each
    // id inside its loop, found the sibling already gone, threw KeyNotFoundException, and
    // BulkOperationRunner counted that as a failure - so a batch that fully succeeded reported
    // "Resolved 1 issues (1 failed)". Fails against the pre-fix service, which reports (1, 1).
    [TestMethod]
    public async Task ResolveIssues_SiblingIssueCascadedAwayByAnEarlierResolve_IsNotCountedAsAFailure()
    {
        const long audiobookId = 7;
        var book = MakeMissingFileBook(audiobookId, "/library/gone/missing.m4b");

        var missingFileIssue = new ConsistencyIssue
        {
            Id = 1,
            AudiobookId = audiobookId,
            Audiobook = book,
            IssueType = ConsistencyIssueType.MissingMediaFile,
            Description = "missing",
        };
        var opfIssue = new ConsistencyIssue
        {
            Id = 2,
            AudiobookId = audiobookId,
            Audiobook = book,
            IssueType = ConsistencyIssueType.MissingOpfFile,
            Description = "no opf",
        };

        _issueRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync(new List<ConsistencyIssue> { missingFileIssue, opfIssue });

        var (resolved, failed) = await _service.ResolveIssues(new[] { 1L, 2L });

        Assert.AreEqual(1, resolved, "the missing-media resolve removed the book and all its issues");
        Assert.AreEqual(0, failed, "the sibling it cascaded away is not a failure");

        // The cascaded sibling must not have been attempted at all.
        _tagHandler.Verify(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()), Times.Never);
    }

    // The same shape for the narrower sidecar cascade: WriteMetadata writes desc.txt, reader.txt
    // and metadata.opf together, so resolving any one of them settles the other two.
    [TestMethod]
    public async Task ResolveIssues_SidecarSiblingsForOneBook_ResolveOnceAndReportNoFailures()
    {
        const long audiobookId = 9;
        var path = Path.Combine(Path.GetTempPath(), $"consistency-{Guid.NewGuid():N}.m4b");
        await File.WriteAllTextAsync(path, "fake audio");

        try
        {
            var book = MakeMissingFileBook(audiobookId, path);
            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
                .Returns(new Domain.Audiobook(
                    new List<Domain.Person> { new("Author") }, "Book 9", 2024,
                    new Domain.AudiobookFileInfo(path, Path.GetFileName(path), 1000)));

            var descIssue = new ConsistencyIssue
            {
                Id = 1,
                AudiobookId = audiobookId,
                Audiobook = book,
                IssueType = ConsistencyIssueType.MissingDescTxt,
                Description = "no desc",
            };
            var opfIssue = new ConsistencyIssue
            {
                Id = 2,
                AudiobookId = audiobookId,
                Audiobook = book,
                IssueType = ConsistencyIssueType.MissingOpfFile,
                Description = "no opf",
            };

            _issueRepository
                .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<long>>()))
                .ReturnsAsync(new List<ConsistencyIssue> { descIssue, opfIssue });

            var (resolved, failed) = await _service.ResolveIssues(new[] { 1L, 2L });

            Assert.AreEqual(1, resolved);
            Assert.AreEqual(0, failed);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            foreach (var sidecar in new[] { "desc.txt", "reader.txt", "metadata.opf" })
            {
                var sidecarPath = Path.Combine(directory, sidecar);
                if (File.Exists(sidecarPath)) File.Delete(sidecarPath);
            }
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // An unrelated book in the same batch must still be resolved - the skip is per audiobook,
    // not a blanket stop. Guards against "fixing" the false failure by simply bailing out.
    [TestMethod]
    public async Task ResolveIssues_OtherBooksInTheBatch_AreStillResolved()
    {
        var bookA = MakeMissingFileBook(1, "/library/gone/a.m4b");
        var bookB = MakeMissingFileBook(2, "/library/gone/b.m4b");

        var issues = new List<ConsistencyIssue>
        {
            new() { Id = 1, AudiobookId = 1, Audiobook = bookA, IssueType = ConsistencyIssueType.MissingMediaFile, Description = "a" },
            new() { Id = 2, AudiobookId = 1, Audiobook = bookA, IssueType = ConsistencyIssueType.MissingOpfFile, Description = "a-opf" },
            new() { Id = 3, AudiobookId = 2, Audiobook = bookB, IssueType = ConsistencyIssueType.MissingMediaFile, Description = "b" },
        };

        _issueRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync(issues);

        var (resolved, failed) = await _service.ResolveIssues(new[] { 1L, 2L, 3L });

        Assert.AreEqual(2, resolved);
        Assert.AreEqual(0, failed);
        _audiobookRepository.Verify(r => r.DeleteAudiobookAsync(1), Times.Once);
        _audiobookRepository.Verify(r => r.DeleteAudiobookAsync(2), Times.Once);
    }

    // Regression test for the N+1: ResolveIssuesByType loaded every issue *with* its audiobook
    // graph, threw that away, and re-fetched each one by id with the same includes. Fails
    // against the pre-fix service, which calls GetByIdAsync once per issue.
    [TestMethod]
    public async Task ResolveIssuesByType_DoesNotRefetchEachIssueById()
    {
        var issues = new List<ConsistencyIssue>
        {
            new() { Id = 1, AudiobookId = 1, Audiobook = MakeMissingFileBook(1, "/library/gone/a.m4b"), IssueType = ConsistencyIssueType.MissingMediaFile, Description = "a" },
            new() { Id = 2, AudiobookId = 2, Audiobook = MakeMissingFileBook(2, "/library/gone/b.m4b"), IssueType = ConsistencyIssueType.MissingMediaFile, Description = "b" },
        };

        _issueRepository.Setup(r => r.GetByTypeAsync(ConsistencyIssueType.MissingMediaFile))
            .ReturnsAsync(issues);

        var (resolved, failed) = await _service.ResolveIssuesByType(nameof(ConsistencyIssueType.MissingMediaFile));

        Assert.AreEqual(2, resolved);
        Assert.AreEqual(0, failed);
        _issueRepository.Verify(r => r.GetByIdAsync(It.IsAny<long>()), Times.Never);
    }

    // The single-issue endpoint must still 404 on an id that genuinely does not exist - the
    // tolerance above is scoped to the bulk path.
    [TestMethod]
    public async Task ResolveIssue_UnknownId_StillThrows()
    {
        _issueRepository.Setup(r => r.GetByIdAsync(It.IsAny<long>())).ReturnsAsync((ConsistencyIssue?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => _service.ResolveIssue(404));
    }

    #endregion

    [TestMethod]
    public async Task RunConsistencyCheck_MissingFile_ReportsIssue()
    {
        var dbAudiobook = new DbAudiobook(
            1, "Test Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
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

        _issueRepository.Verify(r => r.InsertRangeAsync(It.Is<IEnumerable<ConsistencyIssue>>(issues => issues.Any(i =>
            i.IssueType == ConsistencyIssueType.MissingMediaFile &&
            i.AudiobookId == 1
        ))), Times.Once);

        Assert.AreEqual(1, progressCalls.Count);
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
                "A great book", null, null, null, null, null, null, null, null,
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
            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
                .Returns(parsed);

            var progressCalls = new List<(string message, int booksChecked, int total, int issues)>();
            Func<string, int, int, int, Task> progressAction = (msg, bc, t, i) =>
            {
                progressCalls.Add((msg, bc, t, i));
                return Task.CompletedTask;
            };

            await _service.RunConsistencyCheck(progressAction);

            // File exists, so MissingMediaFile should NOT be inserted
            _issueRepository.Verify(r => r.InsertRangeAsync(It.Is<IEnumerable<ConsistencyIssue>>(issues => issues.Any(i =>
                i.IssueType == ConsistencyIssueType.MissingMediaFile
            ))), Times.Never);

            Assert.AreEqual(1, progressCalls.Count);
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
            null, null, null, null, null, null, null, null, null,
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
    public async Task ResolveIssue_MissingMediaFile_FileReappeared_DoesNotDeleteAudiobook()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            await File.WriteAllTextAsync(tempFile, "the file is actually here now");

            var dbAudiobook = new DbAudiobook(
                1, "Test Book", null, null, null, 2024,
                null, null, null, null, null, null, null, null, null,
                tempFile, "test.m4b", 1000);

            var issue = new ConsistencyIssue
            {
                Id = 11,
                AudiobookId = 1,
                Audiobook = dbAudiobook,
                IssueType = ConsistencyIssueType.MissingMediaFile,
                Description = "File missing",
                DetectedAt = DateTime.UtcNow
            };

            _issueRepository.Setup(r => r.GetByIdAsync(11)).ReturnsAsync(issue);

            await _service.ResolveIssue(11);

            _audiobookRepository.Verify(r => r.DeleteAudiobookAsync(It.IsAny<long>()), Times.Never);
            _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(It.IsAny<long>()), Times.Never);
            _issueRepository.Verify(r => r.DeleteAsync(11), Times.Once);
            Assert.IsTrue(File.Exists(tempFile), "the reappeared file should not be touched");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
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
                "desc", null, null, null, null, null, null, null, null,
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

            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(parsed);

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
    public async Task RunConsistencyCheck_TagValueDiffersFromFile_ReportsTagMismatch()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            await File.WriteAllTextAsync(tempFile, "fake audio content");

            var dbAudiobook = new DbAudiobook(
                1, "Test Book", null, "Series", "0.5", 2024,
                null, null, null, null, null, null, null, null, null,
                tempFile, "test.m4b", 1000)
            {
                Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author One") }
            };

            _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync())
                .ReturnsAsync(new List<DbAudiobook> { dbAudiobook });

            // Simulates the file's tags having lost the fractional series part (e.g. the
            // Movement Part fallback truncation bug), so the value on disk no longer matches
            // what the library metadata says it should be.
            var parsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author One") },
                "Test Book",
                2024,
                new Domain.AudiobookFileInfo(tempFile, "test.m4b", 1000))
            {
                Series = "Series",
                SeriesPart = "0"
            };

            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(parsed);

            var progressCalls = new List<(string message, int booksChecked, int total, int issues)>();
            Func<string, int, int, int, Task> progressAction = (msg, bc, t, i) =>
            {
                progressCalls.Add((msg, bc, t, i));
                return Task.CompletedTask;
            };

            await _service.RunConsistencyCheck(progressAction);

            _issueRepository.Verify(r => r.InsertRangeAsync(It.Is<IEnumerable<ConsistencyIssue>>(issues => issues.Any(iss =>
                iss.IssueType == ConsistencyIssueType.TagMismatch &&
                iss.AudiobookId == 1 &&
                iss.ExpectedValue!.Contains("Series Part: 0.5") &&
                iss.ActualValue!.Contains("Series Part: 0") &&
                !iss.ActualValue!.Contains("Series Part: 0.5")
            ))), Times.Once);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task RunConsistencyCheck_DescriptionAndGenresDifferFromFile_ReportsTagMismatch()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            await File.WriteAllTextAsync(tempFile, "fake audio content");

            var dbAudiobook = new DbAudiobook(
                1, "Test Book", null, null, null, 2024,
                "DB description", "DB copyright", "DB publisher", "DB language", "DB rating", "DB asin", "DB www", null, null,
                tempFile, "test.m4b", 1000)
            {
                Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author One") },
                Genres = new List<Database.Models.Genre> { new Database.Models.Genre(1, "Fiction") }
            };

            _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync())
                .ReturnsAsync(new List<DbAudiobook> { dbAudiobook });

            // Simulates the m4b tags on disk having drifted from the library metadata for
            // fields that were previously excluded from the tag-mismatch comparison.
            var parsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author One") },
                "Test Book",
                2024,
                new Domain.AudiobookFileInfo(tempFile, "test.m4b", 1000))
            {
                Description = "File description",
                Copyright = "File copyright",
                Publisher = "File publisher",
                Rating = "File rating",
                Asin = "File asin",
                Www = "File www",
                Genres = new List<string> { "Fantasy" }
            };

            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(parsed);

            var progressCalls = new List<(string message, int booksChecked, int total, int issues)>();
            Func<string, int, int, int, Task> progressAction = (msg, bc, t, i) =>
            {
                progressCalls.Add((msg, bc, t, i));
                return Task.CompletedTask;
            };

            await _service.RunConsistencyCheck(progressAction);

            _issueRepository.Verify(r => r.InsertRangeAsync(It.Is<IEnumerable<ConsistencyIssue>>(issues => issues.Any(iss =>
                iss.IssueType == ConsistencyIssueType.TagMismatch &&
                iss.AudiobookId == 1 &&
                iss.ExpectedValue!.Contains("Description: DB description") &&
                iss.ActualValue!.Contains("Description: File description") &&
                iss.ExpectedValue!.Contains("Copyright: DB copyright") &&
                iss.ExpectedValue!.Contains("Publisher: DB publisher") &&
                iss.ExpectedValue!.Contains("Rating: DB rating") &&
                iss.ExpectedValue!.Contains("Asin: DB asin") &&
                iss.ExpectedValue!.Contains("Www: DB www") &&
                iss.ExpectedValue!.Contains("Genres: Fiction") &&
                iss.ActualValue!.Contains("Genres: Fantasy")
            ))), Times.Once);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task ResolveIssue_TagMismatch_RewritesTagsFromDatabaseMetadata()
    {
        var dbAudiobook = new DbAudiobook(
            1, "Test Book", null, "Series", "0.5", 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/test.m4b", "test.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author One") }
        };

        var issue = new ConsistencyIssue
        {
            Id = 40,
            AudiobookId = 1,
            Audiobook = dbAudiobook,
            IssueType = ConsistencyIssueType.TagMismatch,
            Description = "m4b tags do not match library metadata: Series Part",
            DetectedAt = DateTime.UtcNow
        };

        _issueRepository.Setup(r => r.GetByIdAsync(40)).ReturnsAsync(issue);

        // ResolveTagMismatch re-fetches the audiobook itself (with its full includes) rather than
        // relying on issue.Audiobook, which only carries a partial include set (Authors, no Narrators/Genres).
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(1)).ReturnsAsync(dbAudiobook);

        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Domain.Audiobook>()))
            .ReturnsAsync(new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author One") },
                "Test Book",
                2024,
                new Domain.AudiobookFileInfo("/library/test.m4b", "test.m4b", 1000)));

        await _service.ResolveIssue(40);

        // The DB record - not the (possibly corrupted) file tags - is the source of truth,
        // per the "Binding invariant" in CLAUDE.md: only UpdateAudiobook may rewrite these fields.
        _audiobookService.Verify(s => s.UpdateAudiobook(1, It.Is<Domain.Audiobook>(a =>
            a.SeriesPart == "0.5" && a.Series == "Series"
        )), Times.Once);
        _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Once);
    }

    [TestMethod]
    public async Task ResolveIssue_WrongFilePath_MovesFileAndCleansUpOldDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var libraryPath = Path.Combine(tempRoot, "library");
        var oldDir = Path.Combine(tempRoot, "oldauthor", "oldbook");
        Directory.CreateDirectory(oldDir);

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = libraryPath });
            var service = new LibraryConsistencyService(
                settings,
                _audiobookRepository.Object,
                _issueRepository.Object,
                _orphanDirectoryRepository.Object,
                _tagHandler.Object,
                _audiobookService.Object,
                _logger.Object);

            var oldFile = Path.Combine(oldDir, "test.m4b");
            await File.WriteAllTextAsync(oldFile, "fake audio content");
            await File.WriteAllTextAsync(Path.Combine(oldDir, "desc.txt"), "old description");
            await File.WriteAllTextAsync(Path.Combine(oldDir, "reader.txt"), "Old Narrator");
            await File.WriteAllBytesAsync(Path.Combine(oldDir, "cover.jpg"), new byte[] { 0xFF, 0xD8 });

            var parsedOld = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo(oldFile, "test.m4b", 1000));

            var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(parsedOld);
            var expectedFullPath = AudiobookFileHandler.JoinPaths(libraryPath, expectedRelativePath);

            var parsedNew = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo(expectedFullPath, Path.GetFileName(expectedFullPath), 1000))
            {
                Description = "New description",
                Narrators = new List<Domain.Person> { new Domain.Person("New Narrator") }
            };

            _tagHandler.Setup(t => t.ParseAudiobook(It.Is<FileInfo>(f => f.FullName == oldFile), It.IsAny<bool>()))
                .Returns(parsedOld);
            _tagHandler.Setup(t => t.ParseAudiobook(It.Is<FileInfo>(f => f.FullName == expectedFullPath), It.IsAny<bool>()))
                .Returns(parsedNew);

            var dbAudiobook = new DbAudiobook(
                1, "Book", null, null, null, 2024,
                null, null, null, null, null, null, null, null, null,
                oldFile, "test.m4b", 1000);

            var issue = new ConsistencyIssue
            {
                Id = 30,
                AudiobookId = 1,
                Audiobook = dbAudiobook,
                IssueType = ConsistencyIssueType.WrongFilePath,
                Description = "File path does not match expected path from tags",
                DetectedAt = DateTime.UtcNow
            };

            _issueRepository.Setup(r => r.GetByIdAsync(30)).ReturnsAsync(issue);

            await service.ResolveIssue(30);

            Assert.IsTrue(File.Exists(expectedFullPath), "m4b should have been moved to the expected path");
            Assert.IsFalse(File.Exists(oldFile), "old m4b location should no longer exist");

            Assert.IsFalse(File.Exists(Path.Combine(oldDir, "desc.txt")), "leftover desc.txt should be removed from old dir");
            Assert.IsFalse(File.Exists(Path.Combine(oldDir, "reader.txt")), "leftover reader.txt should be removed from old dir");
            Assert.IsFalse(File.Exists(Path.Combine(oldDir, "cover.jpg")), "leftover cover.jpg should be removed from old dir");
            Assert.IsFalse(Directory.Exists(oldDir), "old directory should be removed once it is empty");

            var newDir = Path.GetDirectoryName(expectedFullPath)!;
            Assert.AreEqual("New description", await File.ReadAllTextAsync(Path.Combine(newDir, "desc.txt")));
            Assert.AreEqual("New Narrator", await File.ReadAllTextAsync(Path.Combine(newDir, "reader.txt")));

            _audiobookRepository.Verify(r => r.UpdateFilePathAsync(1, expectedFullPath, Path.GetFileName(expectedFullPath)), Times.Once);
            _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Once);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [TestMethod]
    public async Task ResolveIssue_WrongFilePath_FileMissing_ThrowsFileNotFoundException()
    {
        var dbAudiobook = new DbAudiobook(
            1, "Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/nonexistent/path/test.m4b", "test.m4b", 1000);

        var issue = new ConsistencyIssue
        {
            Id = 31,
            AudiobookId = 1,
            Audiobook = dbAudiobook,
            IssueType = ConsistencyIssueType.WrongFilePath,
            Description = "File path does not match expected path from tags",
            DetectedAt = DateTime.UtcNow
        };

        _issueRepository.Setup(r => r.GetByIdAsync(31)).ReturnsAsync(issue);

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => _service.ResolveIssue(31));

        _audiobookRepository.Verify(r => r.UpdateFilePathAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ResolveIssue_WrongFilePath_PathAlreadyCorrect_SkipsRelocateAndClearsIssue()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var libraryPath = Path.Combine(tempRoot, "library");

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = libraryPath });
            var service = new LibraryConsistencyService(
                settings,
                _audiobookRepository.Object,
                _issueRepository.Object,
                _orphanDirectoryRepository.Object,
                _tagHandler.Object,
                _audiobookService.Object,
                _logger.Object);

            var placeholderParsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo("placeholder.m4b", "placeholder.m4b", 1000));
            var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(placeholderParsed);
            var currentFile = AudiobookFileHandler.JoinPaths(libraryPath, expectedRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(currentFile)!);
            await File.WriteAllTextAsync(currentFile, "fake audio content");

            var parsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo(currentFile, Path.GetFileName(currentFile), 1000));

            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(parsed);

            var dbAudiobook = new DbAudiobook(
                1, "Book", null, null, null, 2024,
                null, null, null, null, null, null, null, null, null,
                currentFile, Path.GetFileName(currentFile), 1000);

            var issue = new ConsistencyIssue
            {
                Id = 32,
                AudiobookId = 1,
                Audiobook = dbAudiobook,
                IssueType = ConsistencyIssueType.WrongFilePath,
                Description = "File path does not match expected path from tags",
                DetectedAt = DateTime.UtcNow
            };

            _issueRepository.Setup(r => r.GetByIdAsync(32)).ReturnsAsync(issue);

            await service.ResolveIssue(32);

            Assert.IsTrue(File.Exists(currentFile), "file should remain untouched at its already-correct path");
            _audiobookRepository.Verify(r => r.UpdateFilePathAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Once);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    // Regression test: the sweep only ever examined *leaf* directories, so a deleted series was
    // cleaned up one level per run - the check flagged "Author/Series/Book", resolving it made
    // "Author/Series" a leaf, and only the next full check noticed. The user had to run the
    // check once per level. Fails against the pre-fix service, which reports the deepest folder
    // rather than the whole reclaimable subtree.
    [TestMethod]
    public async Task RunConsistencyCheck_NestedOrphanedFolders_ReportsTheTopmostReclaimableOneInASinglePass()
    {
        var libraryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var keptBookDir = Path.Combine(libraryPath, "Kept Author", "Kept Book");
        var orphanBookDir = Path.Combine(libraryPath, "Gone Author", "Gone Series", "Gone Book");
        Directory.CreateDirectory(keptBookDir);
        Directory.CreateDirectory(orphanBookDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(keptBookDir, "book.m4b"), "fake audio");
            await File.WriteAllTextAsync(Path.Combine(orphanBookDir, "desc.txt"), "leftover");

            var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = libraryPath });
            var service = new LibraryConsistencyService(
                settings, _audiobookRepository.Object, _issueRepository.Object,
                _orphanDirectoryRepository.Object, _tagHandler.Object, _audiobookService.Object, _logger.Object);

            _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(new List<DbAudiobook>());

            List<OrphanDirectory> insertedDirectories = new();
            _orphanDirectoryRepository.Setup(r => r.InsertRangeAsync(It.IsAny<IEnumerable<OrphanDirectory>>()))
                .Callback<IEnumerable<OrphanDirectory>>(d => insertedDirectories = d.ToList())
                .Returns(Task.CompletedTask);

            await service.RunConsistencyCheck((_, _, _, _) => Task.CompletedTask);

            // The whole "Gone Author" subtree is reclaimable, so that is what is offered - not
            // just its deepest folder, and not each level as a separate issue to resolve.
            CollectionAssert.AreEqual(
                new List<string> { Path.Combine(libraryPath, "Gone Author") },
                insertedDirectories.Select(d => d.DirectoryPath).ToList());
        }
        finally
        {
            Directory.Delete(libraryPath, true);
        }
    }

    // A parent holding a real book must never be swept up with an orphaned sibling folder.
    [TestMethod]
    public async Task RunConsistencyCheck_ParentHoldingAudio_IsNotReportedWithItsOrphanedChild()
    {
        var libraryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var authorDir = Path.Combine(libraryPath, "Author");
        var bookDir = Path.Combine(authorDir, "Real Book");
        var orphanDir = Path.Combine(authorDir, "Leftovers");
        Directory.CreateDirectory(bookDir);
        Directory.CreateDirectory(orphanDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(bookDir, "book.m4b"), "fake audio");
            await File.WriteAllTextAsync(Path.Combine(orphanDir, "desc.txt"), "leftover");

            var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = libraryPath });
            var service = new LibraryConsistencyService(
                settings, _audiobookRepository.Object, _issueRepository.Object,
                _orphanDirectoryRepository.Object, _tagHandler.Object, _audiobookService.Object, _logger.Object);

            _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(new List<DbAudiobook>());

            List<OrphanDirectory> insertedDirectories = new();
            _orphanDirectoryRepository.Setup(r => r.InsertRangeAsync(It.IsAny<IEnumerable<OrphanDirectory>>()))
                .Callback<IEnumerable<OrphanDirectory>>(d => insertedDirectories = d.ToList())
                .Returns(Task.CompletedTask);

            await service.RunConsistencyCheck((_, _, _, _) => Task.CompletedTask);

            CollectionAssert.AreEqual(
                new List<string> { orphanDir },
                insertedDirectories.Select(d => d.DirectoryPath).ToList());
        }
        finally
        {
            Directory.Delete(libraryPath, true);
        }
    }

    [TestMethod]
    public async Task RunConsistencyCheck_DirectoryWithNoAudioFile_ReportsOrphanDirectory()
    {
        var libraryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var bookWithAudioDir = Path.Combine(libraryPath, "Author", "Book With Audio");
        var orphanDir = Path.Combine(libraryPath, "Author", "Orphaned Folder");
        Directory.CreateDirectory(bookWithAudioDir);
        Directory.CreateDirectory(orphanDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(bookWithAudioDir, "book.m4b"), "fake audio");
            await File.WriteAllTextAsync(Path.Combine(orphanDir, "desc.txt"), "leftover");

            var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = libraryPath });
            var service = new LibraryConsistencyService(
                settings,
                _audiobookRepository.Object,
                _issueRepository.Object,
                _orphanDirectoryRepository.Object,
                _tagHandler.Object,
                _audiobookService.Object,
                _logger.Object);

            _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(new List<DbAudiobook>());

            // The sweep inserts all orphans in one batch rather than one SaveChanges per folder.
            List<OrphanDirectory> insertedDirectories = new();
            _orphanDirectoryRepository.Setup(r => r.InsertRangeAsync(It.IsAny<IEnumerable<OrphanDirectory>>()))
                .Callback<IEnumerable<OrphanDirectory>>(d => insertedDirectories = d.ToList())
                .Returns(Task.CompletedTask);

            var progressCalls = new List<(string message, int booksChecked, int total, int issues)>();
            Func<string, int, int, int, Task> progressAction = (msg, bc, t, i) =>
            {
                progressCalls.Add((msg, bc, t, i));
                return Task.CompletedTask;
            };

            await service.RunConsistencyCheck(progressAction);

            _orphanDirectoryRepository.Verify(r => r.InsertRangeAsync(It.IsAny<IEnumerable<OrphanDirectory>>()), Times.Once);
            _orphanDirectoryRepository.Verify(r => r.InsertAsync(It.IsAny<OrphanDirectory>()), Times.Never);
            CollectionAssert.AreEqual(
                new List<string> { orphanDir },
                insertedDirectories.Select(d => d.DirectoryPath).ToList());
            Assert.IsTrue(progressCalls.Exists(c => c.issues == 1));
        }
        finally
        {
            Directory.Delete(libraryPath, true);
        }
    }

    [TestMethod]
    public async Task ResolveOrphanDirectory_DeletesDirectoryAndRemovesEntry()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(Path.Combine(tempDir, "leftover.txt"), "leftover");

        var orphanDirectory = new OrphanDirectory { Id = 5, DirectoryPath = tempDir, DetectedAt = DateTime.UtcNow };
        _orphanDirectoryRepository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(orphanDirectory);

        await _service.ResolveOrphanDirectory(5);

        Assert.IsFalse(Directory.Exists(tempDir));
        _orphanDirectoryRepository.Verify(r => r.DeleteAsync(5), Times.Once);
    }

    [TestMethod]
    public async Task ResolveOrphanDirectory_NotFound_ThrowsKeyNotFound()
    {
        _orphanDirectoryRepository.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((OrphanDirectory?)null);

        var exception = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _service.ResolveOrphanDirectory(999));
        Assert.IsNotNull(exception);
    }

    [TestMethod]
    public async Task ResolveOrphanDirectory_AudioFilePresent_DoesNotDeleteDirectoryButRemovesEntry()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "book.m4b"), "fake audio");

            var orphanDirectory = new OrphanDirectory { Id = 6, DirectoryPath = tempDir, DetectedAt = DateTime.UtcNow };
            _orphanDirectoryRepository.Setup(r => r.GetByIdAsync(6)).ReturnsAsync(orphanDirectory);

            await _service.ResolveOrphanDirectory(6);

            Assert.IsTrue(Directory.Exists(tempDir), "directory containing an audio file should not be deleted");
            _orphanDirectoryRepository.Verify(r => r.DeleteAsync(6), Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task ResolveAllOrphanDirectories_ResolvesAll()
    {
        var tempDir1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempDir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir1);
        Directory.CreateDirectory(tempDir2);

        var directories = new List<OrphanDirectory>
        {
            new OrphanDirectory { Id = 7, DirectoryPath = tempDir1, DetectedAt = DateTime.UtcNow },
            new OrphanDirectory { Id = 8, DirectoryPath = tempDir2, DetectedAt = DateTime.UtcNow }
        };

        _orphanDirectoryRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(directories);
        _orphanDirectoryRepository.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(directories[0]);
        _orphanDirectoryRepository.Setup(r => r.GetByIdAsync(8)).ReturnsAsync(directories[1]);

        var (resolved, failed) = await _service.ResolveAllOrphanDirectories();

        Assert.AreEqual(2, resolved);
        Assert.AreEqual(0, failed);
        Assert.IsFalse(Directory.Exists(tempDir1));
        Assert.IsFalse(Directory.Exists(tempDir2));
    }

    [TestMethod]
    public async Task RecheckAudiobookAsync_MissingFile_ReportsIssueAndClearsPriorIssues()
    {
        var dbAudiobook = new DbAudiobook(
            1, "Test Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/nonexistent/path/test.m4b", "test.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author") }
        };

        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(1)).ReturnsAsync(dbAudiobook);

        var issues = await _service.RecheckAudiobookAsync(1);

        Assert.AreEqual(1, issues.Count);
        Assert.AreEqual(ConsistencyIssueType.MissingMediaFile, issues[0].IssueType);

        _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Once);
        _issueRepository.Verify(r => r.InsertRangeAsync(It.Is<IEnumerable<ConsistencyIssue>>(issues => issues.Any(i =>
            i.IssueType == ConsistencyIssueType.MissingMediaFile &&
            i.AudiobookId == 1
        ))), Times.Once);
    }

    [TestMethod]
    public async Task RecheckAudiobookAsync_PathNowMatches_ReturnsNoIssues()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = tempDir });
            var service = new LibraryConsistencyService(
                settings,
                _audiobookRepository.Object,
                _issueRepository.Object,
                _orphanDirectoryRepository.Object,
                _tagHandler.Object,
                _audiobookService.Object,
                _logger.Object);

            var placeholderParsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo("placeholder.m4b", "placeholder.m4b", 1000));
            var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(placeholderParsed);
            var currentFile = AudiobookFileHandler.JoinPaths(tempDir, expectedRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(currentFile)!);
            await File.WriteAllTextAsync(currentFile, "fake audio content");

            var parsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo(currentFile, Path.GetFileName(currentFile), 1000));

            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(parsed);

            var dbAudiobook = new DbAudiobook(
                1, "Book", null, null, null, 2024,
                null, null, null, null, null, null, null, null, null,
                currentFile, Path.GetFileName(currentFile), 1000)
            {
                Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author") }
            };

            _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(1)).ReturnsAsync(dbAudiobook);

            // metadata.opf is expected unconditionally once the book has tags, so a "no issues"
            // clean state needs it present and matching what the parsed book would produce.
            var opfPath = AudiobookFileHandler.JoinPaths(Path.GetDirectoryName(currentFile)!, "metadata.opf");
            await File.WriteAllTextAsync(opfPath, AudiobookFileHandler.BuildOpfContent(parsed));

            var issues = await service.RecheckAudiobookAsync(1);

            Assert.AreEqual(0, issues.Count);
            _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Once);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task RecheckAudiobookAsync_MissingOpfFile_ReportsMissingOpfFileIssue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = tempDir });
            var service = new LibraryConsistencyService(
                settings,
                _audiobookRepository.Object,
                _issueRepository.Object,
                _orphanDirectoryRepository.Object,
                _tagHandler.Object,
                _audiobookService.Object,
                _logger.Object);

            var placeholderParsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo("placeholder.m4b", "placeholder.m4b", 1000));
            var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(placeholderParsed);
            var currentFile = AudiobookFileHandler.JoinPaths(tempDir, expectedRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(currentFile)!);
            await File.WriteAllTextAsync(currentFile, "fake audio content");
            // No metadata.opf written for this book.

            var parsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo(currentFile, Path.GetFileName(currentFile), 1000));

            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(parsed);

            var dbAudiobook = new DbAudiobook(
                1, "Book", null, null, null, 2024,
                null, null, null, null, null, null, null, null, null,
                currentFile, Path.GetFileName(currentFile), 1000)
            {
                Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author") }
            };

            _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(1)).ReturnsAsync(dbAudiobook);

            var issues = await service.RecheckAudiobookAsync(1);

            Assert.IsTrue(issues.Any(i => i.IssueType == ConsistencyIssueType.MissingOpfFile));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task RecheckAudiobookAsync_StaleOpfFile_ReportsIncorrectOpfFileIssue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = tempDir });
            var service = new LibraryConsistencyService(
                settings,
                _audiobookRepository.Object,
                _issueRepository.Object,
                _orphanDirectoryRepository.Object,
                _tagHandler.Object,
                _audiobookService.Object,
                _logger.Object);

            var placeholderParsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo("placeholder.m4b", "placeholder.m4b", 1000));
            var expectedRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(placeholderParsed);
            var currentFile = AudiobookFileHandler.JoinPaths(tempDir, expectedRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(currentFile)!);
            await File.WriteAllTextAsync(currentFile, "fake audio content");

            var parsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo(currentFile, Path.GetFileName(currentFile), 1000));

            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(parsed);

            var dbAudiobook = new DbAudiobook(
                1, "Book", null, null, null, 2024,
                null, null, null, null, null, null, null, null, null,
                currentFile, Path.GetFileName(currentFile), 1000)
            {
                Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author") }
            };

            _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(1)).ReturnsAsync(dbAudiobook);

            // Stale content: does not match what BuildOpfContent(parsed) would produce.
            var opfPath = AudiobookFileHandler.JoinPaths(Path.GetDirectoryName(currentFile)!, "metadata.opf");
            await File.WriteAllTextAsync(opfPath, "<package><metadata><dc:title>Stale</dc:title></metadata></package>");

            var issues = await service.RecheckAudiobookAsync(1);

            Assert.IsTrue(issues.Any(i => i.IssueType == ConsistencyIssueType.IncorrectOpfFile));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task ResolveIssue_MissingOpfFile_WritesOpfAndClearsIssue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            await File.WriteAllTextAsync(tempFile, "fake audio content");

            var dbAudiobook = new DbAudiobook(
                1, "Test Book", null, null, null, 2024,
                null, null, null, null, null, null, null, null, null,
                tempFile, "test.m4b", 1000)
            {
                Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author") }
            };

            var parsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Test Book",
                2024,
                new Domain.AudiobookFileInfo(tempFile, "test.m4b", 1000));
            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(parsed);

            var issue = new ConsistencyIssue
            {
                Id = 20,
                AudiobookId = 1,
                Audiobook = dbAudiobook,
                IssueType = ConsistencyIssueType.MissingOpfFile,
                Description = "metadata.opf missing",
                DetectedAt = DateTime.UtcNow
            };

            _issueRepository.Setup(r => r.GetByIdAsync(20)).ReturnsAsync(issue);

            await _service.ResolveIssue(20);

            var opfPath = Path.Combine(tempDir, "metadata.opf");
            Assert.IsTrue(File.Exists(opfPath));
            Assert.AreEqual(AudiobookFileHandler.BuildOpfContent(parsed), File.ReadAllText(opfPath));

            _issueRepository.Verify(r => r.DeleteByAudiobookIdAndTypesAsync(1, It.Is<IEnumerable<ConsistencyIssueType>>(types =>
                types.Contains(ConsistencyIssueType.MissingOpfFile) &&
                types.Contains(ConsistencyIssueType.IncorrectOpfFile)
            )), Times.Once);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task RecheckAudiobookAsync_AudiobookNotFound_ThrowsKeyNotFound()
    {
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(999)).ReturnsAsync((DbAudiobook?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _service.RecheckAudiobookAsync(999));
    }

    [TestMethod]
    public async Task ResolveIssue_NotFound_ThrowsKeyNotFound()
    {
        _issueRepository.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ConsistencyIssue?)null);

        var exception = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _service.ResolveIssue(999));
        Assert.IsNotNull(exception);
    }
}
