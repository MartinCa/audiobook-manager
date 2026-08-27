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
        _audiobookRepository.Setup(r => r.GetAllFilePathsAsync()).ReturnsAsync(new HashSet<string>());

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

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>())).Returns(MakeParsedAudiobook(filePath));

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

        _discoveredAudiobookRepository.Verify(r => r.InsertAsync(It.Is<DiscoveredAudiobook>(d =>
            d.FileInfoFullPath == filePath && d.BookName == "A Book")), Times.Once);

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
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>())).Returns(MakeParsedAudiobook(filePath));

        await _service.ScanLibrary((_, __, ___) => Task.CompletedTask);

        _discoveredAudiobookRepository.Verify(r => r.ClearAllAsync(), Times.Once);
    }

    [TestMethod]
    public async Task ScanLibrary_AlreadyTrackedFile_IsSkippedAndReportedAsTracked()
    {
        var filePath = Path.Combine(_libraryPath, "known-book.m4b");
        await File.WriteAllTextAsync(filePath, "fake audio");

        _audiobookRepository.Setup(r => r.GetAllFilePathsAsync())
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
        _tagHandler.Verify(t => t.ParseAudiobook(It.IsAny<FileInfo>()), Times.Never);

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

        _audiobookRepository.Setup(r => r.GetAllFilePathsAsync())
            .ReturnsAsync(new HashSet<string> { trackedFile });
        _tagHandler.Setup(t => t.ParseAudiobook(It.Is<FileInfo>(f => f.FullName == newFile)))
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

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>()))
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
    public async Task ScanLibrary_MultipleFiles_ProgressReportsIncrementingScannedCountAgainstFixedTotal()
    {
        var file1 = Path.Combine(_libraryPath, "a.m4b");
        var file2 = Path.Combine(_libraryPath, "b.m4b");
        await File.WriteAllTextAsync(file1, "fake audio");
        await File.WriteAllTextAsync(file2, "fake audio");

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>()))
            .Returns<FileInfo>(fi => MakeParsedAudiobook(fi.FullName));

        var progressCalls = new List<(int scanned, int total)>();
        Func<string, int, int, Task> progressAction = (_, scanned, total) =>
        {
            progressCalls.Add((scanned, total));
            return Task.CompletedTask;
        };

        await _service.ScanLibrary(progressAction);

        Assert.AreEqual(2, progressCalls.Count);
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, progressCalls.Select(c => c.scanned).ToList());
        Assert.IsTrue(progressCalls.All(c => c.total == 2));
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
    public async Task IsDuplicateTargetAsync_FileAlreadyAtGeneratedPath_ReturnsTrue()
    {
        var targetPath = Path.Combine(_libraryPath, "existing.m4b");
        await File.WriteAllTextAsync(targetPath, "already there");

        var discovered = new DiscoveredAudiobook("A Book", "/import/book.m4b", "book.m4b", 1000, DateTime.UtcNow)
        {
            Authors = "Author One",
            Year = 2022
        };

        _audiobookService.Setup(s => s.GenerateLibraryPath(It.IsAny<DomainAudiobook>())).Returns(targetPath);

        var isDuplicate = await _service.IsDuplicateTargetAsync(discovered);

        Assert.IsTrue(isDuplicate);
    }

    [TestMethod]
    public async Task IsDuplicateTargetAsync_NoFileAtGeneratedPath_ReturnsFalse()
    {
        var targetPath = Path.Combine(_libraryPath, "not-there.m4b");

        var discovered = new DiscoveredAudiobook("A Book", "/import/book.m4b", "book.m4b", 1000, DateTime.UtcNow)
        {
            Authors = "Author One",
            Year = 2022
        };

        _audiobookService.Setup(s => s.GenerateLibraryPath(It.IsAny<DomainAudiobook>())).Returns(targetPath);

        var isDuplicate = await _service.IsDuplicateTargetAsync(discovered);

        Assert.IsFalse(isDuplicate);
    }

    [TestMethod]
    public async Task IsDuplicateTargetAsync_EntryMissingRequiredTags_ReturnsFalseWithoutThrowing()
    {
        var discovered = new DiscoveredAudiobook("A Book", "/import/book.m4b", "book.m4b", 1000, DateTime.UtcNow)
        {
            Authors = null,
            Year = 2022
        };

        var isDuplicate = await _service.IsDuplicateTargetAsync(discovered);

        Assert.IsFalse(isDuplicate);
        _audiobookService.Verify(s => s.GenerateLibraryPath(It.IsAny<DomainAudiobook>()), Times.Never);
    }
}
