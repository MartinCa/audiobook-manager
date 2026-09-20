using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Options;
using Moq;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Test.Services;

[TestClass]
public class LibraryScanOrchestratorTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IDiscoveredAudiobookRepository> _discoveredAudiobookRepository = null!;
    private Mock<ILibraryTreeWalker> _treeWalker = null!;
    private Mock<ILibraryScanService> _scanService = null!;
    private Mock<ILibraryConsistencyService> _consistencyService = null!;
    private string _libraryPath = null!;
    private LibraryScanOrchestrator _orchestrator = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _discoveredAudiobookRepository = new Mock<IDiscoveredAudiobookRepository>();
        _treeWalker = new Mock<ILibraryTreeWalker>();
        _scanService = new Mock<ILibraryScanService>();
        _consistencyService = new Mock<ILibraryConsistencyService>();

        // A real directory: the orchestrator refuses outright when the configured library path
        // is not there, so the default fixture has to look like a mounted library.
        _libraryPath = Path.Combine(Path.GetTempPath(), $"abm-scan-orc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_libraryPath);

        _discoveredAudiobookRepository.Setup(r => r.ClearAllAsync()).Returns(Task.CompletedTask);
        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(new List<DbAudiobook>());
        _treeWalker.Setup(w => w.Walk(It.IsAny<string>(), It.IsAny<Func<FileInfo, bool>>()))
            .Returns(new LibraryWalkResult(
                new List<AudiobookFileInfo>(),
                new List<LibraryDirectory>()));
        SetupScanFilesAsyncDefault();
        _consistencyService
            .Setup(s => s.RunConsistencyCheck(It.IsAny<Func<string, int, int, int, Task>>(), It.IsAny<ConsistencyCheckInput>()))
            .ReturnsAsync((Func<string, int, int, int, Task> _, ConsistencyCheckInput input) =>
                (input.Audiobooks.Count, 0));

        _orchestrator = new LibraryScanOrchestrator(
            Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = _libraryPath }),
            _audiobookRepository.Object,
            _discoveredAudiobookRepository.Object,
            _treeWalker.Object,
            _scanService.Object,
            _consistencyService.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_libraryPath))
        {
            Directory.Delete(_libraryPath, recursive: true);
        }
    }

    private void SetupScanFilesAsyncDefault()
    {
        _scanService
            .Setup(s => s.ScanFilesAsync(
                It.IsAny<IReadOnlyList<AudiobookFileInfo>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<Func<string, int, int, Task>>()))
            .ReturnsAsync((IReadOnlyList<AudiobookFileInfo> files, IReadOnlyCollection<string> _, Func<string, int, int, Task> __) =>
                (files.Count, files.Count, 0));
    }

    private static DbAudiobook MakeTrackedBook(long id, string fullPath) => new(
        id, $"Book {id}", null, null, null, 2024,
        null, null, null, null, null, null, null, null, null,
        fullPath, Path.GetFileName(fullPath), 1000);

    private static AudiobookFileInfo MakeFile(string fullPath) =>
        new AudiobookFileInfo(fullPath, Path.GetFileName(fullPath), 1000);

    private static IReadOnlyList<LibraryDirectory> SomeDirectories(string root, params string[] subdirs) =>
        subdirs.Select(sub => new LibraryDirectory(
            Path.Combine(root, sub),
            new List<string>(),
            false,
            false)).ToList();

    [TestMethod]
    public async Task RunCombinedScan_WalksTheLibraryExactlyOnce()
    {
        // The point of the orchestrator: the walk that used to happen once for the scan and
        // again for the orphan sweep happens exactly once. Wired as two walks this fails on the
        // Times.Once verification below.
        Func<FileInfo, bool>? capturedFilter = null;
        _treeWalker.Setup(w => w.Walk(It.IsAny<string>(), It.IsAny<Func<FileInfo, bool>>()))
            .Returns((string root, Func<FileInfo, bool> filter) =>
            {
                capturedFilter = filter;
                return new LibraryWalkResult(new List<AudiobookFileInfo>(), new List<LibraryDirectory>());
            });

        await _orchestrator.RunCombinedScanAsync(
            (_, _, _) => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask);

        _treeWalker.Verify(w => w.Walk(It.IsAny<string>(), It.IsAny<Func<FileInfo, bool>>()), Times.Once);

        // The filter handed to the walk is the supported-audio filter, so the walk collects what
        // the scan used to collect.
        Assert.IsNotNull(capturedFilter);
        Assert.IsTrue(capturedFilter!(new FileInfo(Path.Combine(_libraryPath, "book.m4b"))));
        Assert.IsFalse(capturedFilter!(new FileInfo(Path.Combine(_libraryPath, "book.mp3"))));
        Assert.IsFalse(capturedFilter!(new FileInfo(Path.Combine(_libraryPath, "notes.txt"))));
    }

    [TestMethod]
    public async Task RunCombinedScan_LoadsTheTrackedGraphExactlyOnce()
    {
        var tracked = MakeTrackedBook(1, Path.Combine(_libraryPath, "tracked.m4b"));
        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(new List<DbAudiobook> { tracked });

        await _orchestrator.RunCombinedScanAsync(
            (_, _, _) => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask);

        _audiobookRepository.Verify(r => r.GetAllWithIncludesAsync(), Times.Once);
    }

    [TestMethod]
    public async Task RunCombinedScan_UntrackedAndTrackedFiles_ClassifiesEachSupportedFileOnce()
    {
        // One walk result carrying an untracked file and a tracked one; the known-path set
        // derives from the tracked graph, and the whole supported list goes to the scan in a
        // single call (which decides per file whether to parse it).
        var untracked = MakeFile(Path.Combine(_libraryPath, "new.m4b"));
        var trackedPath = Path.Combine(_libraryPath, "tracked.m4b");
        var tracked = MakeFile(trackedPath);

        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync())
            .ReturnsAsync(new List<DbAudiobook> { MakeTrackedBook(1, trackedPath) });
        _treeWalker.Setup(w => w.Walk(It.IsAny<string>(), It.IsAny<Func<FileInfo, bool>>()))
            .Returns(new LibraryWalkResult(new List<AudiobookFileInfo> { untracked, tracked }, new List<LibraryDirectory>()));

        IReadOnlyList<AudiobookFileInfo>? scannedFiles = null;
        IReadOnlyCollection<string>? scannedKnownPaths = null;
        _scanService
            .Setup(s => s.ScanFilesAsync(
                It.IsAny<IReadOnlyList<AudiobookFileInfo>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<Func<string, int, int, Task>>()))
            .Returns(async (
                IReadOnlyList<AudiobookFileInfo> files,
                IReadOnlyCollection<string> knownPaths,
                Func<string, int, int, Task> _) =>
            {
                scannedFiles = files;
                scannedKnownPaths = knownPaths;
                return (files.Count, 1, files.Count - 1);
            });

        var result = await _orchestrator.RunCombinedScanAsync(
            (_, _, _) => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask);

        // One classification pass over exactly the walked files - not one walk per file, and no
        // second walk re-reading the tree.
        _scanService.Verify(s => s.ScanFilesAsync(
            It.IsAny<IReadOnlyList<AudiobookFileInfo>>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<Func<string, int, int, Task>>()), Times.Once);
        CollectionAssert.AreEquivalent(
            new[] { untracked.FullPath, trackedPath },
            scannedFiles!.Select(f => f.FullPath).ToList());
        Assert.IsTrue(scannedKnownPaths!.Contains(trackedPath), "the tracked path must be known so the scan skips it");
        Assert.IsFalse(scannedKnownPaths!.Contains(untracked.FullPath), "the untracked path must not be known so the scan parses it");

        Assert.AreEqual(2, result.TotalFiles);
        Assert.AreEqual(1, result.NewFiles);
        Assert.AreEqual(1, result.AlreadyTracked);
    }

    [TestMethod]
    public async Task RunCombinedScan_PassesDirectoriesToConsistencySoTheOrphanSweepDoesNotWalkAgain()
    {
        var loadedGraph = new List<DbAudiobook> { MakeTrackedBook(1, Path.Combine(_libraryPath, "tracked.m4b")) };
        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(loadedGraph);

        var directories = SomeDirectories(_libraryPath, "Author", "Author/Series");
        _treeWalker.Setup(w => w.Walk(It.IsAny<string>(), It.IsAny<Func<FileInfo, bool>>()))
            .Returns(new LibraryWalkResult(new List<AudiobookFileInfo>(), directories));

        ConsistencyCheckInput? capturedInput = null;
        _consistencyService
            .Setup(s => s.RunConsistencyCheck(It.IsAny<Func<string, int, int, int, Task>>(), It.IsAny<ConsistencyCheckInput>()))
            .Returns(async (Func<string, int, int, int, Task> _, ConsistencyCheckInput input) =>
            {
                capturedInput = input;
                return (input.Audiobooks.Count, 2);
            });

        var result = await _orchestrator.RunCombinedScanAsync(
            (_, _, _) => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask);

        Assert.IsNotNull(capturedInput);
        Assert.AreSame(loadedGraph, capturedInput!.Audiobooks, "the already-loaded graph, not a second load");
        CollectionAssert.AreEquivalent(
            directories.Select(d => d.Path).ToList(),
            capturedInput!.Directories.Select(d => d.Path).ToList());
        Assert.AreEqual(2, result.IssuesFound);
        Assert.AreEqual(1, result.BooksChecked);
    }

    [TestMethod]
    public async Task RunCombinedScan_ReturnsTheCombinedResult()
    {
        _scanService
            .Setup(s => s.ScanFilesAsync(
                It.IsAny<IReadOnlyList<AudiobookFileInfo>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<Func<string, int, int, Task>>()))
            .ReturnsAsync((IReadOnlyList<AudiobookFileInfo> files, IReadOnlyCollection<string> _, Func<string, int, int, Task> __) =>
                (10, 4, 6));
        _consistencyService
            .Setup(s => s.RunConsistencyCheck(It.IsAny<Func<string, int, int, int, Task>>(), It.IsAny<ConsistencyCheckInput>()))
            .ReturnsAsync((Func<string, int, int, int, Task> _, ConsistencyCheckInput __) => (9, 2));

        var result = await _orchestrator.RunCombinedScanAsync(
            (_, _, _) => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask);

        Assert.AreEqual(new CombinedScanResult(10, 4, 6, 9, 2), result);
    }

    [TestMethod]
    public async Task RunCombinedScan_LibraryMissing_ThrowsAndClearsNothing()
    {
        var settings = Options.Create(new AudiobookManagerSettings
        {
            AudiobookLibraryPath = Path.Combine(Path.GetTempPath(), $"abm-orc-not-mounted-{Guid.NewGuid()}")
        });
        var orchestrator = new LibraryScanOrchestrator(
            settings,
            _audiobookRepository.Object,
            _discoveredAudiobookRepository.Object,
            _treeWalker.Object,
            _scanService.Object,
            _consistencyService.Object);

        var ex = await Assert.ThrowsExactlyAsync<LibraryUnavailableException>(
            () => orchestrator.RunCombinedScanAsync(
                (_, _, _) => Task.CompletedTask,
                (_, _, _, _) => Task.CompletedTask));

        StringAssert.Contains(ex.Message, "is not available");

        // Critically: it refuses *before* wiping the previously discovered rows.
        _discoveredAudiobookRepository.Verify(r => r.ClearAllAsync(), Times.Never);
        _audiobookRepository.Verify(r => r.GetAllWithIncludesAsync(), Times.Never);
        _treeWalker.Verify(w => w.Walk(It.IsAny<string>(), It.IsAny<Func<FileInfo, bool>>()), Times.Never);
        _scanService.Verify(s => s.ScanFilesAsync(
            It.IsAny<IReadOnlyList<AudiobookFileInfo>>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<Func<string, int, int, Task>>()), Times.Never);
        _consistencyService.Verify(s => s.RunConsistencyCheck(
            It.IsAny<Func<string, int, int, int, Task>>(), It.IsAny<ConsistencyCheckInput>()), Times.Never);
    }

    [TestMethod]
    public async Task RunCombinedScan_ClearsDiscoveredEntriesBeforeScanning()
    {
        var order = new List<string>();
        _discoveredAudiobookRepository.Setup(r => r.ClearAllAsync())
            .Callback(() => order.Add("clear"))
            .Returns(Task.CompletedTask);
        _scanService
            .Setup(s => s.ScanFilesAsync(
                It.IsAny<IReadOnlyList<AudiobookFileInfo>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<Func<string, int, int, Task>>()))
            .Returns(async (
                IReadOnlyList<AudiobookFileInfo> files,
                IReadOnlyCollection<string> _,
                Func<string, int, int, Task> __) =>
            {
                order.Add("scan");
                return (files.Count, files.Count, 0);
            });

        await _orchestrator.RunCombinedScanAsync(
            (_, _, _) => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask);

        CollectionAssert.AreEqual(new[] { "clear", "scan" }, order,
            "the discovered table must be empty before classification starts");
    }

    [TestMethod]
    public async Task RunCombinedScan_BuildsTheKnownPathSetWithTheOsAwarePathComparer()
    {
        // Regression: the known-path set used to be built with the default (always
        // case-sensitive) comparer, so on Windows/macOS a tracked book whose stored path differed
        // only in case was re-reported as newly discovered on every scan. The orchestrator
        // derives the set with AudiobookFileHandler.PathComparer - the same OS-aware comparer the
        // scan's old GetAllFilePathsAsync load used.
        //
        // The comparer is asserted directly rather than through a case-variant probe. That probe
        // (the earlier form of this test) was vacuous on Linux: PathComparer is
        // StringComparer.Ordinal there, so a case-only variant is genuinely a different file and
        // the `Contains(caseVariant)`/`PathsEqual` assertions were always false - the test could
        // never fail. A truly cross-platform case-folding test is not possible on Linux, where
        // the file system (and therefore the comparer) is genuinely case-sensitive - so this
        // asserts the exact comparer the set was constructed with instead.
        var storedPath = Path.Combine(_libraryPath, "tracked.m4b");

        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync())
            .ReturnsAsync(new List<DbAudiobook> { MakeTrackedBook(1, storedPath) });
        _treeWalker.Setup(w => w.Walk(It.IsAny<string>(), It.IsAny<Func<FileInfo, bool>>()))
            .Returns(new LibraryWalkResult(
                new List<AudiobookFileInfo> { MakeFile(Path.Combine(_libraryPath, "other.m4b")) },
                new List<LibraryDirectory>()));

        IReadOnlyCollection<string>? capturedKnownPaths = null;
        _scanService
            .Setup(s => s.ScanFilesAsync(
                It.IsAny<IReadOnlyList<AudiobookFileInfo>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<Func<string, int, int, Task>>()))
            .Returns(async (
                IReadOnlyList<AudiobookFileInfo> files,
                IReadOnlyCollection<string> knownPaths,
                Func<string, int, int, Task> _) =>
            {
                capturedKnownPaths = knownPaths;
                return (files.Count, 0, files.Count);
            });

        await _orchestrator.RunCombinedScanAsync(
            (_, _, _) => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask);

        Assert.IsNotNull(capturedKnownPaths);
        Assert.IsInstanceOfType<HashSet<string>>(capturedKnownPaths,
            "the known-path set must stay a HashSet so membership is O(1) per file");
        Assert.AreSame(AudiobookFileHandler.PathComparer, ((HashSet<string>)capturedKnownPaths!).Comparer,
            "case handling (and therefore whether a case-only variant is re-discovered) has to "
            + "follow the file system's own rule, which PathComparer captures per OS");
        Assert.IsTrue(capturedKnownPaths!.Contains(storedPath),
            "the tracked path must be in the set so the scan skips it");
    }

    [TestMethod]
    public async Task RunCombinedScan_DiscoveryCompletedCallback_ReceivesRealCountsEvenWhenConsistencyThrows()
    {
        // The controller reports the scan's completion as soon as discovery finishes, so a failure
        // in the consistency half must not be able to erase the discovery counts the user can
        // already see. Without the early callback the only completion send happens after the whole
        // combined run returns, and on error the runner's zeroed completion would clobber the real
        // discovery result.
        _scanService
            .Setup(s => s.ScanFilesAsync(
                It.IsAny<IReadOnlyList<AudiobookFileInfo>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<Func<string, int, int, Task>>()))
            .ReturnsAsync((IReadOnlyList<AudiobookFileInfo> files, IReadOnlyCollection<string> _, Func<string, int, int, Task> __) =>
                (10, 4, 6));
        _consistencyService
            .Setup(s => s.RunConsistencyCheck(It.IsAny<Func<string, int, int, int, Task>>(), It.IsAny<ConsistencyCheckInput>()))
            .ThrowsAsync(new Exception("consistency boom"));

        var capturedCounts = new List<int>();
        await Assert.ThrowsExactlyAsync<Exception>(() => _orchestrator.RunCombinedScanAsync(
            (_, _, _) => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask,
            (totalFiles, newFiles, alreadyTracked) =>
            {
                capturedCounts.Add(totalFiles);
                capturedCounts.Add(newFiles);
                capturedCounts.Add(alreadyTracked);
                return Task.CompletedTask;
            }));

        CollectionAssert.AreEqual(new[] { 10, 4, 6 }, capturedCounts,
            "the real discovery counts must reach the callback before the consistency failure propagates");
    }
}
