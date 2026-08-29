using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;
using DomainAudiobookFileInfo = AudiobookManager.Domain.AudiobookFileInfo;
using DomainPerson = AudiobookManager.Domain.Person;

namespace AudiobookManager.Test.Services;

[TestClass]
public class LibraryScanServiceTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IDiscoveredAudiobookRepository> _discoveredAudiobookRepository = null!;
    private Mock<IAudiobookTagHandler> _tagHandler = null!;
    private Mock<IAudiobookService> _audiobookService = null!;
    private Mock<ILogger<LibraryScanService>> _logger = null!;
    private string _libraryPath = null!;
    private LibraryScanService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _discoveredAudiobookRepository = new Mock<IDiscoveredAudiobookRepository>();
        _tagHandler = new Mock<IAudiobookTagHandler>();
        _audiobookService = new Mock<IAudiobookService>();
        _logger = new Mock<ILogger<LibraryScanService>>();

        _libraryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_libraryPath);

        var settings = Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = _libraryPath });

        _discoveredAudiobookRepository.Setup(r => r.ClearAllAsync()).Returns(Task.CompletedTask);
        _audiobookRepository.Setup(r => r.GetAllFilePathsAsync(It.IsAny<StringComparer>())).ReturnsAsync(new HashSet<string>());

        _service = new LibraryScanService(
            settings,
            _audiobookRepository.Object,
            _discoveredAudiobookRepository.Object,
            _tagHandler.Object,
            _audiobookService.Object,
            _logger.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_libraryPath))
        {
            Directory.Delete(_libraryPath, true);
        }
    }

    private static DomainAudiobook MakeParsedAudiobook(string filePath, string bookName = "A Book") =>
        new DomainAudiobook(
            new List<DomainPerson> { new DomainPerson("Author") },
            bookName,
            2020,
            new DomainAudiobookFileInfo(filePath, Path.GetFileName(filePath), 1000));

    [TestMethod]
    public async Task ScanLibrary_NewFile_IsInsertedAsDiscoveredAndCounted()
    {
        var filePath = Path.Combine(_libraryPath, "new-book.m4b");
        await File.WriteAllTextAsync(filePath, "fake audio");

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(MakeParsedAudiobook(filePath));

        var progressCalls = new List<(string message, int scanned, int total)>();
        Func<string, int, int, Task> progressAction = (msg, scanned, total) =>
        {
            progressCalls.Add((msg, scanned, total));
            return Task.CompletedTask;
        };

        var (totalFiles, newFiles, trackedFiles) = await _service.ScanLibrary(progressAction);

        Assert.AreEqual(1, totalFiles);
        Assert.AreEqual(1, newFiles);
        Assert.AreEqual(0, trackedFiles);

        // Inserted as part of a batch rather than one transaction per file.
        _discoveredAudiobookRepository.Verify(r => r.InsertRangeAsync(It.Is<IEnumerable<DiscoveredAudiobook>>(batch =>
            batch.Count(d => d.FileInfoFullPath == filePath && d.BookName == "A Book") == 1)), Times.Once);
        _discoveredAudiobookRepository.Verify(r => r.InsertAsync(It.IsAny<DiscoveredAudiobook>()), Times.Never);

        Assert.AreEqual(1, progressCalls.Count);
        Assert.IsTrue(progressCalls[0].message.StartsWith("Discovered:"));
        Assert.AreEqual(1, progressCalls[0].scanned);
        Assert.AreEqual(1, progressCalls[0].total);
    }

    [TestMethod]
    public async Task ScanLibrary_ClearsPreviouslyDiscoveredEntriesBeforeScanning()
    {
        var filePath = Path.Combine(_libraryPath, "book.m4b");
        await File.WriteAllTextAsync(filePath, "fake audio");
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(MakeParsedAudiobook(filePath));

        await _service.ScanLibrary((_, __, ___) => Task.CompletedTask);

        _discoveredAudiobookRepository.Verify(r => r.ClearAllAsync(), Times.Once);
    }

    [TestMethod]
    public async Task ScanLibrary_AlreadyTrackedFile_IsSkippedAndReportedAsTracked()
    {
        var filePath = Path.Combine(_libraryPath, "known-book.m4b");
        await File.WriteAllTextAsync(filePath, "fake audio");

        _audiobookRepository.Setup(r => r.GetAllFilePathsAsync(It.IsAny<StringComparer>()))
            .ReturnsAsync(new HashSet<string> { filePath });

        var progressCalls = new List<(string message, int scanned, int total)>();
        Func<string, int, int, Task> progressAction = (msg, scanned, total) =>
        {
            progressCalls.Add((msg, scanned, total));
            return Task.CompletedTask;
        };

        var (totalFiles, newFiles, trackedFiles) = await _service.ScanLibrary(progressAction);

        Assert.AreEqual(1, totalFiles);
        Assert.AreEqual(0, newFiles);
        Assert.AreEqual(1, trackedFiles);

        _discoveredAudiobookRepository.Verify(r => r.InsertAsync(It.IsAny<DiscoveredAudiobook>()), Times.Never);
        _tagHandler.Verify(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()), Times.Never);

        Assert.AreEqual(1, progressCalls.Count);
        Assert.IsTrue(progressCalls[0].message.StartsWith("Already tracked:"));
    }

    [TestMethod]
    public async Task ScanLibrary_MixOfNewAndTrackedFiles_ReportsCorrectTotals()
    {
        var trackedFile = Path.Combine(_libraryPath, "tracked.m4b");
        var newFile = Path.Combine(_libraryPath, "new.m4b");
        await File.WriteAllTextAsync(trackedFile, "fake audio");
        await File.WriteAllTextAsync(newFile, "fake audio");

        _audiobookRepository.Setup(r => r.GetAllFilePathsAsync(It.IsAny<StringComparer>()))
            .ReturnsAsync(new HashSet<string> { trackedFile });
        _tagHandler.Setup(t => t.ParseAudiobook(It.Is<FileInfo>(f => f.FullName == newFile), It.IsAny<bool>()))
            .Returns(MakeParsedAudiobook(newFile));

        var (totalFiles, newFiles, trackedFiles) = await _service.ScanLibrary((_, __, ___) => Task.CompletedTask);

        Assert.AreEqual(2, totalFiles);
        Assert.AreEqual(1, newFiles);
        Assert.AreEqual(1, trackedFiles);
    }

    [TestMethod]
    public async Task ScanLibrary_NonAudioFile_IsIgnoredEntirely()
    {
        await File.WriteAllTextAsync(Path.Combine(_libraryPath, "notes.txt"), "not audio");

        var (totalFiles, newFiles, trackedFiles) = await _service.ScanLibrary((_, __, ___) => Task.CompletedTask);

        Assert.AreEqual(0, totalFiles);
        Assert.AreEqual(0, newFiles);
        Assert.AreEqual(0, trackedFiles);
    }

    [TestMethod]
    public async Task ScanLibrary_ParseFailure_IsHandledGracefullyAndCountedAsNotNew()
    {
        var filePath = Path.Combine(_libraryPath, "corrupt.m4b");
        await File.WriteAllTextAsync(filePath, "fake audio");

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Throws(new UnsupportedFormatException("bad file"));

        var progressCalls = new List<(string message, int scanned, int total)>();
        Func<string, int, int, Task> progressAction = (msg, scanned, total) =>
        {
            progressCalls.Add((msg, scanned, total));
            return Task.CompletedTask;
        };

        var (totalFiles, newFiles, trackedFiles) = await _service.ScanLibrary(progressAction);

        Assert.AreEqual(1, totalFiles);
        Assert.AreEqual(0, newFiles);
        Assert.AreEqual(1, trackedFiles);

        _discoveredAudiobookRepository.Verify(r => r.InsertAsync(It.IsAny<DiscoveredAudiobook>()), Times.Never);
        Assert.AreEqual(1, progressCalls.Count);
        Assert.IsTrue(progressCalls[0].message.StartsWith("Error parsing:"));
    }

    [TestMethod]
    public async Task ScanLibrary_MultipleFiles_ReportsFinalProgressAgainstFixedTotal()
    {
        var file1 = Path.Combine(_libraryPath, "a.m4b");
        var file2 = Path.Combine(_libraryPath, "b.m4b");
        await File.WriteAllTextAsync(file1, "fake audio");
        await File.WriteAllTextAsync(file2, "fake audio");

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => MakeParsedAudiobook(fi.FullName));

        var progressCalls = new List<(int scanned, int total)>();
        Func<string, int, int, Task> progressAction = (_, scanned, total) =>
        {
            progressCalls.Add((scanned, total));
            return Task.CompletedTask;
        };

        await _service.ScanLibrary(progressAction);

        // Progress is broadcast every 25 files plus once at the end, so a two-file scan reports
        // exactly once - at completion, against the fixed total.
        Assert.AreEqual(1, progressCalls.Count);
        Assert.AreEqual(2, progressCalls[0].scanned);
        Assert.AreEqual(2, progressCalls[0].total);
    }

    // Regression test: the scan used to await a SignalR broadcast for every single file (and on
    // the already-tracked branch too), so a large library sent one hub message per book -
    // thousands the client throttles away unseen. Fails against the unbatched code, which
    // reported 60 times here.
    [TestMethod]
    public async Task ScanLibrary_ManyFiles_DoesNotBroadcastProgressPerFile()
    {
        const int fileCount = 60;
        for (var i = 0; i < fileCount; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_libraryPath, $"book-{i:D3}.m4b"), "fake audio");
        }

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => MakeParsedAudiobook(fi.FullName));

        var progressCalls = new List<int>();
        Func<string, int, int, Task> progressAction = (_, scanned, _) =>
        {
            progressCalls.Add(scanned);
            return Task.CompletedTask;
        };

        var (totalFiles, newFiles, _) = await _service.ScanLibrary(progressAction);

        Assert.AreEqual(fileCount, totalFiles);
        Assert.AreEqual(fileCount, newFiles);

        // Every 25th file, then the final one: 25, 50, 60.
        CollectionAssert.AreEqual(new[] { 25, 50, 60 }, progressCalls);
    }

    // Batching must not make one database failure poison the rest of the scan. The batch insert
    // sat inside the per-file try/catch, so a failing batch was logged as "Failed to parse
    // audiobook at <the current file>" - blaming a file that parsed fine - and `pending` was
    // never cleared, so every subsequent batch retried the same rows and threw again.
    [TestMethod]
    public async Task ScanLibrary_BatchInsertFails_KeepsScanningAndDoesNotRetryTheFailedRows()
    {
        const int fileCount = 30;
        for (var i = 0; i < fileCount; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_libraryPath, $"book-{i:D3}.m4b"), "fake audio");
        }

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => MakeParsedAudiobook(fi.FullName));

        var attemptedBatches = new List<int>();
        _discoveredAudiobookRepository
            .Setup(r => r.InsertRangeAsync(It.IsAny<IEnumerable<DiscoveredAudiobook>>()))
            .Callback((IEnumerable<DiscoveredAudiobook> batch) => attemptedBatches.Add(batch.Count()))
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var (totalFiles, _, _) = await _service.ScanLibrary((_, _, _) => Task.CompletedTask);

        Assert.AreEqual(fileCount, totalFiles, "the scan completes rather than aborting");
        // Exactly one attempt: the rows are dropped after it fails, not retried forever.
        Assert.AreEqual(1, attemptedBatches.Count);
    }

    // Regression test: one InsertAsync (and therefore one SaveChanges/transaction) per file
    // meant a first scan of a large library ran a transaction per book. Fails against the
    // unbatched code, which never called InsertRangeAsync at all.
    [TestMethod]
    public async Task ScanLibrary_ManyFiles_InsertsInBatchesRatherThanOneTransactionPerFile()
    {
        const int fileCount = 60;
        for (var i = 0; i < fileCount; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_libraryPath, $"book-{i:D3}.m4b"), "fake audio");
        }

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => MakeParsedAudiobook(fi.FullName));

        var batchSizes = new List<int>();
        _discoveredAudiobookRepository
            .Setup(r => r.InsertRangeAsync(It.IsAny<IEnumerable<DiscoveredAudiobook>>()))
            .Callback((IEnumerable<DiscoveredAudiobook> batch) => batchSizes.Add(batch.Count()))
            .Returns(Task.CompletedTask);

        await _service.ScanLibrary((_, _, _) => Task.CompletedTask);

        _discoveredAudiobookRepository.Verify(r => r.InsertAsync(It.IsAny<DiscoveredAudiobook>()), Times.Never);
        Assert.AreEqual(fileCount, batchSizes.Sum());
        // Well under one write per file - 60 books fit in a single 200-row batch.
        Assert.AreEqual(1, batchSizes.Count);
    }

    [TestMethod]
    public async Task BulkImportAsync_OrganizesEachDiscoveredBookAndDeletesTheDiscoveredEntry()
    {
        var discovered = new DiscoveredAudiobook("A Book", "/import/book.m4b", "book.m4b", 1000, DateTime.UtcNow)
        {
            Id = 5,
            Authors = "Author One",
            Year = 2022
        };

        _discoveredAudiobookRepository.Setup(r => r.GetByPathsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new List<DiscoveredAudiobook> { discovered });

        _audiobookService.Setup(s => s.OrganizeAudiobook(It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()))
            .ReturnsAsync((DomainAudiobook a, Func<string, int, Task> _) => a);

        var (processed, succeeded, failed) = await _service.BulkImportAsync(
            new List<string> { "/import/book.m4b" },
            (_, __, ___, ____) => Task.CompletedTask);

        Assert.AreEqual(1, processed);
        Assert.AreEqual(1, succeeded);
        Assert.AreEqual(0, failed);

        _audiobookService.Verify(s => s.OrganizeAudiobook(
            It.Is<DomainAudiobook>(a => a.BookName == "A Book" && a.Year == 2022),
            It.IsAny<Func<string, int, Task>>()), Times.Once);
        _discoveredAudiobookRepository.Verify(r => r.DeleteAsync(5), Times.Once);
    }

    // Regression: the discovered row had no language column, so importing built a domain object
    // with Language == null. SaveAudiobookTagsToFile assigns track.Language unconditionally, so
    // the import wiped the language tag the scan had just read off the file.
    [TestMethod]
    public async Task BulkImportAsync_PreservesLanguageTagCapturedAtScanTime()
    {
        var discovered = new DiscoveredAudiobook("A Book", "/import/book.m4b", "book.m4b", 1000, DateTime.UtcNow)
        {
            Id = 7,
            Authors = "Author One",
            Year = 2022,
            Language = "English"
        };

        _discoveredAudiobookRepository.Setup(r => r.GetByPathsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new List<DiscoveredAudiobook> { discovered });

        DomainAudiobook? organized = null;
        _audiobookService.Setup(s => s.OrganizeAudiobook(It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()))
            .Callback((DomainAudiobook a, Func<string, int, Task> _) => organized = a)
            .ReturnsAsync((DomainAudiobook a, Func<string, int, Task> _) => a);

        var (_, succeeded, failed) = await _service.BulkImportAsync(
            new List<string> { "/import/book.m4b" },
            (_, __, ___, ____) => Task.CompletedTask);

        Assert.AreEqual(1, succeeded);
        Assert.AreEqual(0, failed);
        Assert.IsNotNull(organized);
        Assert.AreEqual("English", organized!.Language);
    }

    // The other half of the same round trip: the scan has to record the tag in the first place.
    [TestMethod]
    public async Task ScanLibrary_RecordsTheLanguageTagOnTheDiscoveredRow()
    {
        var filePath = Path.Combine(_libraryPath, "with-language.m4b");
        await File.WriteAllTextAsync(filePath, "fake audio");

        var parsed = MakeParsedAudiobook(filePath);
        parsed.Language = "German";
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(parsed);

        var inserted = new List<DiscoveredAudiobook>();
        _discoveredAudiobookRepository
            .Setup(r => r.InsertRangeAsync(It.IsAny<IEnumerable<DiscoveredAudiobook>>()))
            .Callback((IEnumerable<DiscoveredAudiobook> batch) => inserted.AddRange(batch))
            .Returns(Task.CompletedTask);

        await _service.ScanLibrary((_, _, _) => Task.CompletedTask);

        Assert.AreEqual(1, inserted.Count);
        Assert.AreEqual("German", inserted[0].Language);
    }

    [TestMethod]
    public async Task BulkImportAsync_DiscoveredEntryMissingRequiredTags_IsCountedAsFailedWithoutAborting()
    {
        var missingAuthor = new DiscoveredAudiobook("Book Without Author", "/import/no-author.m4b", "no-author.m4b", 1000, DateTime.UtcNow)
        {
            Id = 1,
            Authors = null,
            Year = 2020
        };
        var valid = new DiscoveredAudiobook("Valid Book", "/import/valid.m4b", "valid.m4b", 1000, DateTime.UtcNow)
        {
            Id = 2,
            Authors = "Author",
            Year = 2020
        };

        _discoveredAudiobookRepository.Setup(r => r.GetByPathsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new List<DiscoveredAudiobook> { missingAuthor, valid });

        _audiobookService.Setup(s => s.OrganizeAudiobook(It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()))
            .ReturnsAsync((DomainAudiobook a, Func<string, int, Task> _) => a);

        var (processed, succeeded, failed) = await _service.BulkImportAsync(
            new List<string> { "/import/no-author.m4b", "/import/valid.m4b" },
            (_, __, ___, ____) => Task.CompletedTask);

        Assert.AreEqual(2, processed);
        Assert.AreEqual(1, succeeded);
        Assert.AreEqual(1, failed);

        _discoveredAudiobookRepository.Verify(r => r.DeleteAsync(2), Times.Once);
        _discoveredAudiobookRepository.Verify(r => r.DeleteAsync(1), Times.Never);
    }

    [TestMethod]
    public async Task BulkImportAsync_PathNotFoundAmongDiscovered_IsCountedAsFailed()
    {
        _discoveredAudiobookRepository.Setup(r => r.GetByPathsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new List<DiscoveredAudiobook>());

        var (processed, succeeded, failed) = await _service.BulkImportAsync(
            new List<string> { "/import/missing.m4b" },
            (_, __, ___, ____) => Task.CompletedTask);

        Assert.AreEqual(1, processed);
        Assert.AreEqual(0, succeeded);
        Assert.AreEqual(1, failed);
    }

    [TestMethod]
    public async Task BulkImportAsync_OrganizeThrows_InvokesOnItemFailedWithPathAndMessageBeforeCountingFailure()
    {
        var discovered = new DiscoveredAudiobook("A Book", "/import/book.m4b", "book.m4b", 1000, DateTime.UtcNow)
        {
            Id = 5,
            Authors = "Author One",
            Year = 2022
        };

        _discoveredAudiobookRepository.Setup(r => r.GetByPathsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new List<DiscoveredAudiobook> { discovered });

        _audiobookService.Setup(s => s.OrganizeAudiobook(It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()))
            .ThrowsAsync(new Exception("'/library/book.m4b' already exists"));

        var failures = new List<(string Path, string Error)>();

        var (processed, succeeded, failed) = await _service.BulkImportAsync(
            new List<string> { "/import/book.m4b" },
            (_, __, ___, ____) => Task.CompletedTask,
            (path, error) =>
            {
                failures.Add((path, error));
                return Task.CompletedTask;
            });

        Assert.AreEqual(1, processed);
        Assert.AreEqual(0, succeeded);
        Assert.AreEqual(1, failed);

        Assert.AreEqual(1, failures.Count);
        Assert.AreEqual("/import/book.m4b", failures[0].Path);
        Assert.AreEqual("'/library/book.m4b' already exists", failures[0].Error);

        _discoveredAudiobookRepository.Verify(r => r.DeleteAsync(5), Times.Never);
    }

    [TestMethod]
    public async Task IsDuplicateTarget_FileAlreadyAtGeneratedPath_ReturnsTrue()
    {
        var targetPath = Path.Combine(_libraryPath, "existing.m4b");
        await File.WriteAllTextAsync(targetPath, "already there");

        var discovered = new DiscoveredAudiobook("A Book", "/import/book.m4b", "book.m4b", 1000, DateTime.UtcNow)
        {
            Authors = "Author One",
            Year = 2022
        };

        _audiobookService.Setup(s => s.GenerateLibraryPath(It.IsAny<DomainAudiobook>())).Returns(targetPath);

        var isDuplicate = _service.IsDuplicateTarget(discovered);

        Assert.IsTrue(isDuplicate);
    }

    [TestMethod]
    public async Task IsDuplicateTarget_NoFileAtGeneratedPath_ReturnsFalse()
    {
        var targetPath = Path.Combine(_libraryPath, "not-there.m4b");

        var discovered = new DiscoveredAudiobook("A Book", "/import/book.m4b", "book.m4b", 1000, DateTime.UtcNow)
        {
            Authors = "Author One",
            Year = 2022
        };

        _audiobookService.Setup(s => s.GenerateLibraryPath(It.IsAny<DomainAudiobook>())).Returns(targetPath);

        var isDuplicate = _service.IsDuplicateTarget(discovered);

        Assert.IsFalse(isDuplicate);
    }

    [TestMethod]
    public async Task IsDuplicateTarget_TargetPathIsTheDiscoveredFileItself_ReturnsFalse()
    {
        var targetPath = Path.Combine(_libraryPath, "existing.m4b");
        await File.WriteAllTextAsync(targetPath, "already there");

        var discovered = new DiscoveredAudiobook("A Book", targetPath, "existing.m4b", 1000, DateTime.UtcNow)
        {
            Authors = "Author One",
            Year = 2022
        };

        _audiobookService.Setup(s => s.GenerateLibraryPath(It.IsAny<DomainAudiobook>())).Returns(targetPath);

        var isDuplicate = _service.IsDuplicateTarget(discovered);

        Assert.IsFalse(isDuplicate);
    }

    [TestMethod]
    public async Task IsDuplicateTarget_EntryMissingRequiredTags_ReturnsFalseWithoutThrowing()
    {
        var discovered = new DiscoveredAudiobook("A Book", "/import/book.m4b", "book.m4b", 1000, DateTime.UtcNow)
        {
            Authors = null,
            Year = 2022
        };

        var isDuplicate = _service.IsDuplicateTarget(discovered);

        Assert.IsFalse(isDuplicate);
        _audiobookService.Verify(s => s.GenerateLibraryPath(It.IsAny<DomainAudiobook>()), Times.Never);
    }

    [TestMethod]
    public async Task ScanLibrary_RequestsTheKnownPathSetWithTheOsAwarePathComparer()
    {
        // Regression: the known-path set was built with the default (always case-sensitive)
        // comparer, so on Windows/macOS a tracked book whose stored path differed only in case
        // was re-reported as newly discovered on every scan - and could then be imported twice.
        var filePath = Path.Combine(_libraryPath, "book.m4b");
        await File.WriteAllTextAsync(filePath, "fake audio");

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns(MakeParsedAudiobook(filePath));

        await _service.ScanLibrary((_, _, _) => Task.CompletedTask);

        _audiobookRepository.Verify(
            r => r.GetAllFilePathsAsync(AudiobookFileHandler.PathComparer),
            Times.Once);
    }

    [TestMethod]
    public async Task ScanLibrary_TrackedFileDifferingOnlyInCase_IsSkippedOnCaseInsensitiveFileSystems()
    {
        var filePath = Path.Combine(_libraryPath, "Known-Book.m4b");
        await File.WriteAllTextAsync(filePath, "fake audio");

        // The DB records the same file under a different case, as it would after a rename.
        var storedPath = Path.Combine(_libraryPath, "known-book.m4b");
        _audiobookRepository.Setup(r => r.GetAllFilePathsAsync(It.IsAny<StringComparer>()))
            .ReturnsAsync((StringComparer? comparer) =>
                new HashSet<string>(new[] { storedPath }, comparer ?? StringComparer.Ordinal));

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns(MakeParsedAudiobook(filePath));

        var (totalFiles, newFiles, trackedFiles) = await _service.ScanLibrary((_, _, _) => Task.CompletedTask);

        Assert.AreEqual(1, totalFiles);

        // On a case-insensitive file system the two paths are the same file, so nothing is new.
        var caseInsensitive = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        Assert.AreEqual(caseInsensitive ? 0 : 1, newFiles);
        Assert.AreEqual(caseInsensitive ? 1 : 0, trackedFiles);
    }

    [TestMethod]
    public async Task ScanLibrary_ParsesWithoutCoverData()
    {
        // The discovered row stores no cover, so base64-encoding a multi-megabyte picture per
        // file during a full library scan is pure waste.
        var filePath = Path.Combine(_libraryPath, "book.m4b");
        await File.WriteAllTextAsync(filePath, "fake audio");

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns(MakeParsedAudiobook(filePath));

        await _service.ScanLibrary((_, _, _) => Task.CompletedTask);

        _tagHandler.Verify(t => t.ParseAudiobook(It.IsAny<FileInfo>(), false), Times.Once);
        _tagHandler.Verify(t => t.ParseAudiobook(It.IsAny<FileInfo>(), true), Times.Never);
    }

    [TestMethod]
    public async Task BulkImportAsync_GenreWithComma_PreservesCommaInGenre()
    {
        var filePath = Path.Combine(_libraryPath, "book.m4b");
        var discovered = new DiscoveredAudiobook("A Book", filePath, "book.m4b", 1000, DateTime.UtcNow)
        {
            Id = 1,
            Authors = "Author",
            Year = 2020,
            Genres = "Film, Stage & Screen/Adventure"
        };

        _discoveredAudiobookRepository.Setup(r => r.GetByPathsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new List<DiscoveredAudiobook> { discovered });

        DomainAudiobook? organizedBook = null;
        _audiobookService.Setup(s => s.OrganizeAudiobook(It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()))
            .Callback<DomainAudiobook, Func<string, int, Task>>((book, _) => organizedBook = book)
            .ReturnsAsync(MakeParsedAudiobook(filePath));

        await _service.BulkImportAsync(
            new List<string> { filePath },
            (_, _, _, _) => Task.CompletedTask);

        Assert.IsNotNull(organizedBook);
        Assert.AreEqual(2, organizedBook.Genres.Count);
        Assert.AreEqual("Film, Stage & Screen", organizedBook.Genres[0]);
        Assert.AreEqual("Adventure", organizedBook.Genres[1]);
    }

    [TestMethod]
    public async Task BulkImportAsync_PathCasingDifference_UsesPathComparer()
    {
        var storedPath = Path.Combine(_libraryPath, "Book.m4b");
        var requestedPath = Path.Combine(_libraryPath, "book.m4b");
        var discovered = new DiscoveredAudiobook("A Book", storedPath, "Book.m4b", 1000, DateTime.UtcNow)
        {
            Id = 1,
            Authors = "Author",
            Year = 2020
        };

        _discoveredAudiobookRepository.Setup(r => r.GetByPathsAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(new List<DiscoveredAudiobook> { discovered });

        _audiobookService.Setup(s => s.OrganizeAudiobook(It.IsAny<DomainAudiobook>(), It.IsAny<Func<string, int, Task>>()))
            .ReturnsAsync(MakeParsedAudiobook(storedPath));

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            var (processed, succeeded, failed) = await _service.BulkImportAsync(
                new List<string> { requestedPath },
                (_, _, _, _) => Task.CompletedTask);

            Assert.AreEqual(1, succeeded);
            Assert.AreEqual(0, failed);
        }
        else
        {
            var (processed, succeeded, failed) = await _service.BulkImportAsync(
                new List<string> { storedPath },
                (_, _, _, _) => Task.CompletedTask);

            Assert.AreEqual(1, succeeded);
            Assert.AreEqual(0, failed);
        }
    }
}
