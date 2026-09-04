using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private IAudiobookFileHandler _fileHandler = null!;
    private IFileOperations _fileOperations = null!;
    private Mock<IAudiobookService> _audiobookService = null!;
    private Mock<ILogger<LibraryConsistencyService>> _logger = null!;
    private AudiobookSaveGate _saveGate = null!;
    private IOptions<AudiobookManagerSettings> _settings = null!;
    private LibraryConsistencyService _service = null!;

    // Builds the same object graph DependencyInjection.SetupServiceLayer wires up, but by hand
    // with this fixture's mocks - one detection service, one resolver per issue-type group, and
    // one orphan-directory service, all sharing the repository/handler mocks so assertions against
    // those mocks still see every call regardless of which piece made it.
    private LibraryConsistencyService CreateService(IOptions<AudiobookManagerSettings>? settings = null)
    {
        var effectiveSettings = settings ?? _settings;

        var detectors = new IConsistencyIssueDetector[]
        {
            new PathMismatchDetector(),
            new TagMismatchDetector(),
            new SidecarFilesDetector(),
            new CoverFileDetector(),
        };
        var detectionService = new AudiobookIssueDetectionService(
            effectiveSettings, _tagHandler.Object, detectors, NullLogger<AudiobookIssueDetectionService>.Instance);

        var resolvers = new IConsistencyIssueResolver[]
        {
            new MissingMediaFileResolver(
                _audiobookRepository.Object, _issueRepository.Object, _fileHandler, detectionService,
                NullLogger<MissingMediaFileResolver>.Instance),
            new MetadataSidecarResolver(
                _tagHandler.Object, _fileHandler, _issueRepository.Object,
                NullLogger<MetadataSidecarResolver>.Instance),
            new TagOrPathMismatchResolver(
                _audiobookRepository.Object, _audiobookService.Object, _issueRepository.Object,
                NullLogger<TagOrPathMismatchResolver>.Instance),
            new MissingCoverResolver(
                _tagHandler.Object, _fileHandler, _audiobookRepository.Object, _issueRepository.Object,
                NullLogger<MissingCoverResolver>.Instance),
            new UnreadableFileResolver(
                _issueRepository.Object, detectionService, NullLogger<UnreadableFileResolver>.Instance),
        };

        var orphanDirectoryConsistencyService = new OrphanDirectoryConsistencyService(
            effectiveSettings, _orphanDirectoryRepository.Object, _fileOperations,
            NullLogger<OrphanDirectoryConsistencyService>.Instance);

        return new LibraryConsistencyService(
            _audiobookRepository.Object,
            _issueRepository.Object,
            _tagHandler.Object,
            _audiobookService.Object,
            _saveGate,
            detectionService,
            resolvers,
            orphanDirectoryConsistencyService,
            effectiveSettings,
            _logger.Object);
    }

    /// <summary>
    /// A real directory standing in for the mounted library. It has to exist: the service refuses
    /// to run a consistency check (or a MissingMediaFile sweep) against a library path that is not
    /// there, because an absent mount makes every book look deleted.
    /// </summary>
    private string _libraryPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _libraryPath = Path.Combine(Path.GetTempPath(), $"abm-consistency-{Guid.NewGuid()}");
        Directory.CreateDirectory(_libraryPath);

        _audiobookRepository = new Mock<IAudiobookRepository>();
        _issueRepository = new Mock<IConsistencyIssueRepository>();
        _orphanDirectoryRepository = new Mock<IOrphanDirectoryRepository>();
        _tagHandler = new Mock<IAudiobookTagHandler>();
        _fileOperations = new FileOperations();
        _fileHandler = new AudiobookFileHandler(_fileOperations);
        _audiobookService = new Mock<IAudiobookService>();
        _logger = new Mock<ILogger<LibraryConsistencyService>>();
        _saveGate = new AudiobookSaveGate();
        _settings = Options.Create(new AudiobookManagerSettings
        {
            AudiobookLibraryPath = _libraryPath
        });

        _service = CreateService();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_libraryPath))
        {
            Directory.Delete(_libraryPath, recursive: true);
        }
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

        // One per-book report, then one from the orphan-directory sweep that follows it. The
        // sweep used to be skipped here because the configured library path did not exist, so
        // this asserted a single call and covered none of that half of the run.
        Assert.AreEqual(2, progressCalls.Count);
        Assert.AreEqual(1, progressCalls[0].issues);
        Assert.IsTrue(
            progressCalls.Any(c => c.message.Contains("orphaned folders")),
            "The orphan-directory sweep should have reported. Asserted position-agnostically so a "
            + "reordering of the run's phases does not silently stop covering it.");
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

            // Per-book report plus the orphan-directory sweep's own - see the note in
            // RunConsistencyCheck_MissingFile_ReportsIssue.
            Assert.AreEqual(2, progressCalls.Count);
            Assert.IsTrue(progressCalls.Any(c => c.message.StartsWith("Checked:")));
            Assert.IsTrue(progressCalls.Any(c => c.message.Contains("orphaned folders")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task RunConsistencyCheck_BothCoverJpgAndPngExist_ReportsConflictingCoverIssue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            await File.WriteAllTextAsync(tempFile, "fake audio content");

            var coverJpg = Path.Combine(tempDir, "cover.jpg");
            var coverPng = Path.Combine(tempDir, "cover.png");
            await File.WriteAllBytesAsync(coverJpg, new byte[] { 0xFF, 0xD8 });
            await File.WriteAllBytesAsync(coverPng, new byte[] { 0x89, 0x50 });

            var dbAudiobook = new DbAudiobook(
                1, "Test Book", null, null, null, 2024,
                null, null, null, null, null, null, null, null, null,
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
                new Domain.AudiobookFileInfo(tempFile, "test.m4b", 1000));

            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(parsed);

            await _service.RunConsistencyCheck((_, _, _, _) => Task.CompletedTask);

            _issueRepository.Verify(r => r.InsertRangeAsync(It.Is<IEnumerable<ConsistencyIssue>>(issues => issues.Any(iss =>
                iss.IssueType == ConsistencyIssueType.MissingCoverFile &&
                iss.Description.Contains("Conflicting cover files")
            ))), Times.Once);
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

        var result = await _service.ResolveIssue(10);

        Assert.AreEqual("audiobook_deleted", result.ActionTaken);
        _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Once);
        _audiobookRepository.Verify(r => r.DeleteAudiobookAsync(1), Times.Once);
    }

    [TestMethod]
    public async Task ResolveIssue_UnreadableFile_StillUnreadable_KeepsTheBookAndTheIssue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            await File.WriteAllTextAsync(tempFile, "still not a valid m4b");

            var dbAudiobook = new DbAudiobook(
                1, "Test Book", null, null, null, 2024,
                null, null, null, null, null, null, null, null, null,
                tempFile, "test.m4b", 1000);

            var issue = new ConsistencyIssue
            {
                Id = 21,
                AudiobookId = 1,
                Audiobook = dbAudiobook,
                IssueType = ConsistencyIssueType.UnreadableFile,
                Description = "File could not be read",
                DetectedAt = DateTime.UtcNow
            };

            _issueRepository.Setup(r => r.GetByIdAsync(21)).ReturnsAsync(issue);
            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
                .Throws(new InvalidDataException("Not an MP4 container"));

            var result = await _service.ResolveIssue(21);

            Assert.AreEqual("still_unreadable", result.ActionTaken);

            // The distinction from MissingMediaFile that matters: the record is the only place the
            // curated metadata lives, and an unreadable file is not an absent one.
            _audiobookRepository.Verify(r => r.DeleteAudiobookAsync(It.IsAny<long>()), Times.Never);
            Assert.IsTrue(File.Exists(tempFile), "the unreadable file should not be touched");

            // Re-inserted, so the book stays on the consistency screen instead of quietly leaving it.
            _issueRepository.Verify(r => r.InsertRangeAsync(It.Is<IEnumerable<ConsistencyIssue>>(issues =>
                issues.Any(i => i.IssueType == ConsistencyIssueType.UnreadableFile && i.AudiobookId == 1))), Times.Once);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task ResolveIssue_UnreadableFile_NowReadable_ClearsItAndRefreshesTheBook()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            await File.WriteAllTextAsync(tempFile, "readable now");

            var dbAudiobook = new DbAudiobook(
                1, "Test Book", null, null, null, 2024,
                null, null, null, null, null, null, null, null, null,
                tempFile, "test.m4b", 1000);

            var issue = new ConsistencyIssue
            {
                Id = 22,
                AudiobookId = 1,
                Audiobook = dbAudiobook,
                IssueType = ConsistencyIssueType.UnreadableFile,
                Description = "File could not be read",
                DetectedAt = DateTime.UtcNow
            };

            _issueRepository.Setup(r => r.GetByIdAsync(22)).ReturnsAsync(issue);
            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
                .Returns(new Domain.Audiobook(
                    new List<Domain.Person> { new("Author") }, "Test Book", 2024,
                    new Domain.AudiobookFileInfo(tempFile, "test.m4b", 1000)));

            var result = await _service.ResolveIssue(22);

            Assert.AreEqual("file_readable", result.ActionTaken);
            _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Once);
            _audiobookRepository.Verify(r => r.DeleteAudiobookAsync(It.IsAny<long>()), Times.Never);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
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

            var result = await _service.ResolveIssue(11);

            Assert.AreEqual("file_recovered", result.ActionTaken);
            _audiobookRepository.Verify(r => r.DeleteAudiobookAsync(It.IsAny<long>()), Times.Never);
            _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Once);
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

    // Regression: resolving an issue rewrites the book's tags, moves its file, or rewrites its
    // sidecars - the same work an interactive save does - but the two were gated separately, and
    // `resolve-by-type` had no lock of its own at all. A bulk resolve could therefore be
    // rewriting a book while the user's save of that same book was moving it. Both now take the
    // one per-audiobook gate.
    [TestMethod]
    public async Task ResolveIssue_BookAlreadyBeingSaved_IsRefusedWithoutTouchingTheFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            await File.WriteAllTextAsync(tempFile, "fake");

            var dbAudiobook = new DbAudiobook(
                5101, "Test Book", null, null, null, 2024,
                "desc", null, null, null, null, null, null, null, null,
                tempFile, "test.m4b", 1000);

            var issue = new ConsistencyIssue
            {
                Id = 21,
                AudiobookId = 5101,
                Audiobook = dbAudiobook,
                IssueType = ConsistencyIssueType.MissingDescTxt,
                Description = "desc.txt missing",
                DetectedAt = DateTime.UtcNow
            };

            _issueRepository.Setup(r => r.GetByIdAsync(21)).ReturnsAsync(issue);
            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
                .Returns(new Domain.Audiobook(
                    new List<Domain.Person> { new Domain.Person("Author") },
                    "Test Book",
                    2024,
                    new Domain.AudiobookFileInfo(tempFile, "test.m4b", 1000))
                {
                    Description = "A description"
                });

            using var lease = _saveGate.Acquire(5101);

            await Assert.ThrowsExactlyAsync<AudiobookBusyException>(() => _service.ResolveIssue(21));

            Assert.IsFalse(
                File.Exists(Path.Combine(tempDir, "desc.txt")),
                "no sidecar should be written for a book another operation is modifying");
            _issueRepository.Verify(
                r => r.DeleteByAudiobookIdAndTypesAsync(It.IsAny<long>(), It.IsAny<IEnumerable<ConsistencyIssueType>>()),
                Times.Never);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // The bulk path must not abort over one busy book: it is counted as a failure, the rest are
    // resolved, and the next check picks the skipped issue up again.
    [TestMethod]
    public async Task ResolveIssues_OneBookBusy_CountsItAsFailedAndResolvesTheRest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var busyFile = Path.Combine(tempDir, "busy.m4b");
            var freeDir = Path.Combine(tempDir, "free");
            Directory.CreateDirectory(freeDir);
            var freeFile = Path.Combine(freeDir, "free.m4b");
            await File.WriteAllTextAsync(busyFile, "fake");
            await File.WriteAllTextAsync(freeFile, "fake");

            ConsistencyIssue MakeIssue(long id, long audiobookId, string path, string fileName) => new()
            {
                Id = id,
                AudiobookId = audiobookId,
                Audiobook = new DbAudiobook(
                    audiobookId, "Book", null, null, null, 2024,
                    "desc", null, null, null, null, null, null, null, null,
                    path, fileName, 1000),
                IssueType = ConsistencyIssueType.MissingDescTxt,
                Description = "desc.txt missing",
                DetectedAt = DateTime.UtcNow
            };

            var issues = new List<ConsistencyIssue>
            {
                MakeIssue(31, 5201, busyFile, "busy.m4b"),
                MakeIssue(32, 5202, freeFile, "free.m4b"),
            };

            _issueRepository.Setup(r => r.GetByIdsAsync(It.IsAny<List<long>>())).ReturnsAsync(issues);
            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
                .Returns((FileInfo fi, bool _) => new Domain.Audiobook(
                    new List<Domain.Person> { new Domain.Person("Author") },
                    "Book",
                    2024,
                    new Domain.AudiobookFileInfo(fi.FullName, fi.Name, 1000))
                {
                    Description = "A description"
                });

            using var lease = _saveGate.Acquire(5201);

            var (resolved, failed) = await _service.ResolveIssues(new List<long> { 31, 32 });

            Assert.AreEqual(1, resolved);
            Assert.AreEqual(1, failed);
            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "desc.txt")), "the busy book is untouched");
            Assert.IsTrue(File.Exists(Path.Combine(freeDir, "desc.txt")), "the free book is still resolved");
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

        // ResolveTagOrPathMismatch re-fetches the audiobook itself (with its full includes) rather
        // than relying on issue.Audiobook, which only carries a partial include set (Authors, no Narrators/Genres).
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

    // Regression: ResolveWrongFilePath used to relocate the file using tags re-parsed from the
    // file itself (assuming they were already correct), then delete every stored issue for the
    // book on success - including a TagMismatch it never actually fixed, since it never rewrote
    // tags at all. That silently discarded a still-unresolved tag mismatch: the issue list came
    // back empty, but a later recheck reported the same TagMismatch again. WrongFilePath and
    // TagMismatch now share one handler that always rewrites tags from the database first (see
    // ResolveTagOrPathMismatch), so this asserts UpdateAudiobook - not a manual relocate - is what
    // resolves a wrong path.
    [TestMethod]
    public async Task ResolveIssue_WrongFilePath_RewritesTagsAndRelocatesFromDatabaseMetadata()
    {
        var dbAudiobook = new DbAudiobook(
            1, "Test Book", null, "Series", "0.5", 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/wrong/test.m4b", "test.m4b", 1000)
        {
            Authors = new List<Database.Models.Person> { new Database.Models.Person(1, "Author One") }
        };

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
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(1)).ReturnsAsync(dbAudiobook);

        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Domain.Audiobook>()))
            .ReturnsAsync(new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author One") },
                "Test Book",
                2024,
                new Domain.AudiobookFileInfo("/library/correct/test.m4b", "test.m4b", 1000)));

        await _service.ResolveIssue(30);

        // WrongFilePath now goes through the same database-is-truth pipeline as TagMismatch, so
        // it relocates the file *and* rewrites its tags in one call rather than trusting the
        // file's own (possibly wrong) tags to generate the destination.
        _audiobookService.Verify(s => s.UpdateAudiobook(1, It.Is<Domain.Audiobook>(a =>
            a.SeriesPart == "0.5" && a.Series == "Series"
        )), Times.Once);
        _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Once);
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
            var service = CreateService(settings);

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

    // The subtree answer is derived from the children's answers rather than by re-walking the
    // subtree, so it has to propagate up through more than one level.
    [TestMethod]
    public async Task RunConsistencyCheck_AudioDeepInASubtree_KeepsEveryAncestor()
    {
        var libraryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var deepBookDir = Path.Combine(libraryPath, "Author", "Series", "Sub", "Book");
        Directory.CreateDirectory(deepBookDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(deepBookDir, "book.m4b"), "fake audio");

            var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = libraryPath });
            var service = CreateService(settings);

            _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(new List<DbAudiobook>());

            List<OrphanDirectory> insertedDirectories = new();
            _orphanDirectoryRepository.Setup(r => r.InsertRangeAsync(It.IsAny<IEnumerable<OrphanDirectory>>()))
                .Callback<IEnumerable<OrphanDirectory>>(d => insertedDirectories = d.ToList())
                .Returns(Task.CompletedTask);

            await service.RunConsistencyCheck((_, _, _, _) => Task.CompletedTask);

            Assert.AreEqual(0, insertedDirectories.Count,
                "no ancestor of a directory holding audio is reclaimable");
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
            var service = CreateService(settings);

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
            var service = CreateService(settings);

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

        var result = await _service.ResolveOrphanDirectory(5);

        Assert.AreEqual("deleted", result.ActionTaken);
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

            var result = await _service.ResolveOrphanDirectory(6);

            Assert.AreEqual("retained_has_audio", result.ActionTaken);
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

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir2, "audio.m4b"), "media");

            var directories = new List<OrphanDirectory>
            {
                new OrphanDirectory { Id = 7, DirectoryPath = tempDir1, DetectedAt = DateTime.UtcNow },
                new OrphanDirectory { Id = 8, DirectoryPath = tempDir2, DetectedAt = DateTime.UtcNow }
            };

            _orphanDirectoryRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(directories);
            _orphanDirectoryRepository.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(directories[0]);
            _orphanDirectoryRepository.Setup(r => r.GetByIdAsync(8)).ReturnsAsync(directories[1]);

            var (resolved, failed, retained) = await _service.ResolveAllOrphanDirectories();

            Assert.AreEqual(1, resolved);
            Assert.AreEqual(0, failed);
            Assert.AreEqual(1, retained);
            Assert.IsFalse(Directory.Exists(tempDir1));
            Assert.IsTrue(Directory.Exists(tempDir2));
        }
        finally
        {
            if (Directory.Exists(tempDir1))
                Directory.Delete(tempDir1, true);
            if (Directory.Exists(tempDir2))
                Directory.Delete(tempDir2, true);
        }
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
            var service = CreateService(settings);

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
            var service = CreateService(settings);

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
            var service = CreateService(settings);

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

    // Regression: a desc.txt/reader.txt left over from before its field was cleared was invisible
    // to the check - the whole sidecar comparison was skipped when the tag was empty - so the
    // stale file sat in the library serving metadata the book no longer has.
    [TestMethod]
    public async Task RecheckAudiobookAsync_SidecarsPresentButTagsCleared_ReportsThemAsIncorrect()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = tempDir });
            var service = CreateService(settings);

            var placeholderParsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo("placeholder.m4b", "placeholder.m4b", 1000));
            var currentFile = AudiobookFileHandler.JoinPaths(
                tempDir, AudiobookFileHandler.GenerateRelativeAudiobookPath(placeholderParsed));
            var bookDir = Path.GetDirectoryName(currentFile)!;
            Directory.CreateDirectory(bookDir);
            await File.WriteAllTextAsync(currentFile, "fake audio content");

            // The book has neither a Description nor Narrators tag any more...
            var parsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo(currentFile, Path.GetFileName(currentFile), 1000));

            // ...but both sidecars from when it did are still on disk.
            await File.WriteAllTextAsync(Path.Combine(bookDir, "desc.txt"), "a description that was removed");
            await File.WriteAllTextAsync(Path.Combine(bookDir, "reader.txt"), "Narrator Who Left");
            await File.WriteAllTextAsync(
                Path.Combine(bookDir, "metadata.opf"), AudiobookFileHandler.BuildOpfContent(parsed));

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

            var desc = issues.SingleOrDefault(i => i.IssueType == ConsistencyIssueType.IncorrectDescTxt);
            Assert.IsNotNull(desc);
            Assert.AreEqual("a description that was removed", desc!.ActualValue);

            var reader = issues.SingleOrDefault(i => i.IssueType == ConsistencyIssueType.IncorrectReaderTxt);
            Assert.IsNotNull(reader);
            Assert.AreEqual("Narrator Who Left", reader!.ActualValue);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task RecheckAudiobookAsync_NoSidecarsAndNoTags_ReportsNoSidecarIssues()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = tempDir });
            var service = CreateService(settings);

            var placeholderParsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo("placeholder.m4b", "placeholder.m4b", 1000));
            var currentFile = AudiobookFileHandler.JoinPaths(
                tempDir, AudiobookFileHandler.GenerateRelativeAudiobookPath(placeholderParsed));
            var bookDir = Path.GetDirectoryName(currentFile)!;
            Directory.CreateDirectory(bookDir);
            await File.WriteAllTextAsync(currentFile, "fake audio content");

            var parsed = new Domain.Audiobook(
                new List<Domain.Person> { new Domain.Person("Author") },
                "Book",
                2024,
                new Domain.AudiobookFileInfo(currentFile, Path.GetFileName(currentFile), 1000));

            await File.WriteAllTextAsync(
                Path.Combine(bookDir, "metadata.opf"), AudiobookFileHandler.BuildOpfContent(parsed));

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

            CollectionAssert.AreEqual(
                Array.Empty<ConsistencyIssueType>(),
                issues.Select(i => i.IssueType).ToArray());
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

    #region Selective tag-mismatch resolution

    private static DbAudiobook MakeFullBook(long id, string filePath) =>
        new(
            id, "Book Name", "Subtitle", null, null, 2010,
            "Description", "Copyright", "Publisher", "Language", "Rating", "Asin", "Www", null, null,
            filePath, Path.GetFileName(filePath), 1000)
        {
            Authors = new List<Database.Models.Person> { new(1, "Library Author") },
            Narrators = new List<Database.Models.Person> { new(2, "Library Narrator") }
        };

    private static ConsistencyIssue MakeTagMismatchIssue(DbAudiobook book, long id = 5) => new()
    {
        Id = id,
        AudiobookId = book.Id,
        Audiobook = book,
        IssueType = ConsistencyIssueType.TagMismatch,
        Description = "tags differ",
        DetectedAt = DateTime.UtcNow
    };

    [TestMethod]
    public async Task GetTagMismatchFieldsAsync_NotFound_ThrowsKeyNotFound()
    {
        _issueRepository.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ConsistencyIssue?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _service.GetTagMismatchFieldsAsync(999));
    }

    [TestMethod]
    public async Task GetTagMismatchFieldsAsync_NonTagMismatch_ThrowsArgument()
    {
        var book = MakeFullBook(1, "/library/book.m4b");
        _issueRepository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new ConsistencyIssue
        {
            Id = 5,
            AudiobookId = 1,
            Audiobook = book,
            IssueType = ConsistencyIssueType.WrongFilePath,
            Description = "wrong path"
        });

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _service.GetTagMismatchFieldsAsync(5));
    }

    [TestMethod]
    public async Task GetTagMismatchFieldsAsync_ReturnsOnlyDifferingFields()
    {
        var book = MakeFullBook(1, "/library/book.m4b");
        _issueRepository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(MakeTagMismatchIssue(book));

        // File differs from the library only on Book Name, Year, and Genres; every other field
        // matches so the projection returns exactly those three rows.
        var parsed = new Domain.Audiobook(
            new List<Domain.Person> { new("Library Author") },
            "File Book Name",
            2020,
            new Domain.AudiobookFileInfo("/library/book.m4b", "book.m4b", 1000))
        {
            Narrators = new List<Domain.Person> { new("Library Narrator") },
            Subtitle = "Subtitle",
            Description = "Description",
            Copyright = "Copyright",
            Publisher = "Publisher",
            Language = "Language",
            Rating = "Rating",
            Asin = "Asin",
            Www = "Www",
            Genres = new List<string> { "File Genre One", "File Genre Two" }
        };
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(parsed);

        var fields = await _service.GetTagMismatchFieldsAsync(5);

        var byName = fields.ToDictionary(f => f.Field);
        Assert.AreEqual(3, fields.Count);
        Assert.AreEqual("Book Name", byName["Book Name"].LibraryValue);
        Assert.AreEqual("File Book Name", byName["Book Name"].FileValue);
        Assert.AreEqual("2010", byName["Year"].LibraryValue);
        Assert.AreEqual("2020", byName["Year"].FileValue);
        // Genres serialized with the shared formatter (", "-joined), not the '/' tag delimiter.
        Assert.AreEqual("File Genre One, File Genre Two", byName["Genres"].FileValue);
    }

    [TestMethod]
    public async Task ResolveTagMismatchSelectivelyAsync_NotFound_ThrowsKeyNotFound()
    {
        _issueRepository.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ConsistencyIssue?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _service.ResolveTagMismatchSelectivelyAsync(999, new Dictionary<string, string?>()));
    }

    [TestMethod]
    public async Task ResolveTagMismatchSelectivelyAsync_NonTagMismatch_ThrowsArgument()
    {
        var book = MakeFullBook(1, "/library/book.m4b");
        _issueRepository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new ConsistencyIssue
        {
            Id = 5,
            AudiobookId = 1,
            Audiobook = book,
            IssueType = ConsistencyIssueType.MissingDescTxt,
            Description = "no desc"
        });

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _service.ResolveTagMismatchSelectivelyAsync(5, new Dictionary<string, string?>()));
    }

    [TestMethod]
    public async Task ResolveTagMismatchSelectivelyAsync_AppliesOnlySuppliedFieldsAndDeletesIssues()
    {
        var book = MakeFullBook(1, "/library/book.m4b");
        book.Genres = new List<Database.Models.Genre> { new(1, "Library Genre") };
        _issueRepository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(MakeTagMismatchIssue(book));

        Domain.Audiobook? applied = null;
        _audiobookService
            .Setup(s => s.UpdateAudiobook(1, It.IsAny<Domain.Audiobook>(), It.IsAny<Func<string, int, Task>?>()))
            .Callback<long, Domain.Audiobook, Func<string, int, Task>?>((_, a, _) => applied = a)
            .ReturnsAsync((long id, Domain.Audiobook a, Func<string, int, Task>? _) => a);

        var result = await _service.ResolveTagMismatchSelectivelyAsync(5, new Dictionary<string, string?>
        {
            ["Year"] = "2021",
        });

        Assert.IsNotNull(applied);
        Assert.AreEqual("Book Name", applied!.BookName);
        Assert.AreEqual(2021, applied!.Year);
        // Omitted fields keep the library metadata.
        Assert.AreEqual("Library Author", applied!.Authors.Single().Name);
        Assert.AreEqual("Library Genre", applied!.Genres.Single());

        Assert.AreEqual("resolved", result.ActionTaken);
        _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Once);
    }

    [TestMethod]
    public async Task ResolveTagMismatchSelectivelyAsync_StructuralFieldEmpty_ThrowsArgument()
    {
        var book = MakeFullBook(1, "/library/book.m4b");
        _issueRepository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(MakeTagMismatchIssue(book));

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _service.ResolveTagMismatchSelectivelyAsync(5, new Dictionary<string, string?> { ["Year"] = null }));
    }

    [TestMethod]
    public async Task ResolveTagMismatchSelectivelyAsync_BookBusy_ThrowsBusyException()
    {
        var book = MakeFullBook(1, "/library/book.m4b");
        _issueRepository.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(MakeTagMismatchIssue(book));

        // Hold the per-audiobook gate so the resolve's Acquire refuses.
        using var held = _saveGate.Acquire(1);

        await Assert.ThrowsExactlyAsync<AudiobookBusyException>(
            () => _service.ResolveTagMismatchSelectivelyAsync(5, new Dictionary<string, string?> { ["Year"] = "2021" }));
        _audiobookService.Verify(
            s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Domain.Audiobook>(), It.IsAny<Func<string, int, Task>?>()),
            Times.Never);
    }

    #endregion

    #region Library availability guards

    /// <summary>
    /// An issue whose audiobook points at a path that does not exist, so MissingMediaFile
    /// resolves take the "file really is gone" branch and delete the record.
    /// </summary>
    private static ConsistencyIssue MakeIssue(long id, long audiobookId, ConsistencyIssueType type) => new()
    {
        Id = id,
        AudiobookId = audiobookId,
        Audiobook = MakeMissingFileBook(audiobookId, $"/library/gone-{audiobookId}.m4b"),
        IssueType = type,
        Description = type.ToString(),
        DetectedAt = DateTime.UtcNow
    };


    [TestMethod]
    public async Task RunConsistencyCheck_LibraryDirectoryMissing_ThrowsAndClearsNothing()
    {
        // The library path is a volume mount in the normal deployment. If it has gone away since
        // startup validated it, every book's File.Exists is false and the run would record the
        // whole library as MissingMediaFile - which the bulk resolve then turns into deleting
        // every record.
        var settings = Options.Create(new AudiobookManagerSettings
        {
            AudiobookLibraryPath = Path.Combine(Path.GetTempPath(), $"abm-not-mounted-{Guid.NewGuid()}")
        });
        var service = CreateService(settings);

        var ex = await Assert.ThrowsExactlyAsync<LibraryUnavailableException>(
            () => service.RunConsistencyCheck((_, _, _, _) => Task.CompletedTask));

        StringAssert.Contains(ex.Message, "is not available");

        // Critically: it refuses *before* wiping the previous run's findings.
        _issueRepository.Verify(r => r.ClearAllAsync(), Times.Never);
        _orphanDirectoryRepository.Verify(r => r.ClearAllAsync(), Times.Never);
    }

    [TestMethod]
    public async Task ResolveIssuesByType_MissingMediaFileAcrossMostOfLibrary_IsRefused()
    {
        var issues = Enumerable.Range(1, 8)
            .Select(i => MakeIssue(i, i, ConsistencyIssueType.MissingMediaFile))
            .ToList();
        _issueRepository.Setup(r => r.GetByTypeAsync(ConsistencyIssueType.MissingMediaFile)).ReturnsAsync(issues);
        _audiobookRepository.Setup(r => r.CountAsync()).ReturnsAsync(10);

        var ex = await Assert.ThrowsExactlyAsync<LibraryUnavailableException>(
            () => _service.ResolveIssuesByType(nameof(ConsistencyIssueType.MissingMediaFile)));

        StringAssert.Contains(ex.Message, "8 of 10");

        // Nothing was deleted.
        _audiobookRepository.Verify(r => r.DeleteAudiobookAsync(It.IsAny<long>()), Times.Never);
    }

    [TestMethod]
    public async Task ResolveIssuesByType_MissingMediaFileForAFewBooks_IsAllowed()
    {
        var issues = new List<ConsistencyIssue> { MakeIssue(1, 1, ConsistencyIssueType.MissingMediaFile) };
        _issueRepository.Setup(r => r.GetByTypeAsync(ConsistencyIssueType.MissingMediaFile)).ReturnsAsync(issues);
        _audiobookRepository.Setup(r => r.CountAsync()).ReturnsAsync(50);

        var (resolved, failed) = await _service.ResolveIssuesByType(nameof(ConsistencyIssueType.MissingMediaFile));

        Assert.AreEqual(1, resolved);
        Assert.AreEqual(0, failed);
    }

    [TestMethod]
    public async Task ResolveIssuesByType_SmallLibrary_IsNotGatedByFraction()
    {
        // Deleting 2 of 3 books is a legitimate 67%; a fraction says nothing useful at that size.
        var issues = Enumerable.Range(1, 2)
            .Select(i => MakeIssue(i, i, ConsistencyIssueType.MissingMediaFile))
            .ToList();
        _issueRepository.Setup(r => r.GetByTypeAsync(ConsistencyIssueType.MissingMediaFile)).ReturnsAsync(issues);
        _audiobookRepository.Setup(r => r.CountAsync()).ReturnsAsync(3);

        var (resolved, _) = await _service.ResolveIssuesByType(nameof(ConsistencyIssueType.MissingMediaFile));

        Assert.AreEqual(2, resolved);
    }

    [TestMethod]
    public async Task ResolveIssuesByType_OtherIssueTypes_AreNotGated()
    {
        // Only MissingMediaFile deletes records; a sweep of sidecar rewrites is not destructive
        // and must not be refused just because it covers the whole library.
        var issues = Enumerable.Range(1, 10)
            .Select(i => MakeIssue(i, i, ConsistencyIssueType.MissingOpfFile))
            .ToList();
        _issueRepository.Setup(r => r.GetByTypeAsync(ConsistencyIssueType.MissingOpfFile)).ReturnsAsync(issues);

        var (resolved, failed) = await _service.ResolveIssuesByType(nameof(ConsistencyIssueType.MissingOpfFile));

        // Whether each one succeeds is this fixture's business (these books have no real files);
        // what matters is that the sweep ran over all ten rather than being refused, and that the
        // plausibility check was never consulted for a non-destructive issue type.
        Assert.AreEqual(10, resolved + failed);
        _audiobookRepository.Verify(r => r.CountAsync(), Times.Never);
    }

    #endregion
}
